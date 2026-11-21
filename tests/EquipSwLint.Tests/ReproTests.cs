using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace EquipSwLint.Tests;

/// <summary>
/// For every rule: run the flagged pattern and show how it fails on equipment, then run the fixed pattern.
/// Long-running effects (days of uptime, hours of frames) are simulated with injected clocks; threading ones run for real.
/// </summary>
public class ReproTests
{
    [Fact]
    public void Repro_ESL001_DeadlineBreaksAtWrap()
    {
        // PC has been up for 24.9 days: TickCount is 1 s away from int.MaxValue.
        int start = int.MaxValue - 1000;
        int badDeadline = unchecked(start + 3000); // what 'Environment.TickCount + 3000' produces
        int now = unchecked(start + 10);           // only 10 ms later

        bool badExpired = now > badDeadline;        // flagged by ESL001
        bool goodExpired = unchecked(now - start) > 3000;
        long start64 = int.MaxValue - 1000L;
        bool good64Expired = start64 + 10 > start64 + 3000;

        Assert.True(badDeadline < 0);
        Assert.True(badExpired);     // 3 s timeout fires after 10 ms
        Assert.False(goodExpired);
        Assert.False(good64Expired);
    }

    [Fact]
    public void Repro_ESL002_ClockJumpBreaksTimeout()
    {
        // The host syncs the PC clock back by 5 minutes while we wait for a 30 s reply.
        var start = new DateTime(2026, 11, 22, 10, 0, 0);
        var afterSync = start.AddMinutes(-5).AddSeconds(40); // 40 s of real time have passed
        var badElapsed = afterSync - start;                  // flagged by ESL002
        var monotonicElapsed = TimeSpan.FromSeconds(40);     // Stopwatch keeps counting real time

        Assert.True(badElapsed < TimeSpan.Zero);
        Assert.False(badElapsed > TimeSpan.FromSeconds(30)); // timeout never fires for ~5 more minutes
        Assert.True(monotonicElapsed > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Repro_ESL003_SlowCallbackOverlaps()
    {
        int MaxConcurrent(bool oneShot)
        {
            int running = 0, max = 0;
            Timer? timer = null;
            void Callback(object? _)
            {
                var n = Interlocked.Increment(ref running);
                InterlockedMax(ref max, n);
                Thread.Sleep(60); // slow device reply, longer than the 15 ms period
                Interlocked.Decrement(ref running);
                if (oneShot) timer!.Change(15, Timeout.Infinite);
            }
            timer = new Timer(Callback, null, 0, oneShot ? Timeout.Infinite : 15);
            Thread.Sleep(500);
            timer.Dispose();
            Thread.Sleep(100);
            return max;
        }

        Assert.True(MaxConcurrent(oneShot: false) > 1); // two threads talk to the device at once
        Assert.Equal(1, MaxConcurrent(oneShot: true));
    }

    [Fact]
    public async Task Repro_ESL004_SilentDeviceHangsRead()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            // device accepts the connection and then never answers
            using var bad = new TcpClient();
            await bad.ConnectAsync(IPAddress.Loopback, port);
            using var badPeer = await listener.AcceptTcpClientAsync();
            var badRead = Task.Run(() => bad.GetStream().Read(new byte[16], 0, 16)); // flagged by ESL004
            Assert.NotSame(badRead, await Task.WhenAny(badRead, Task.Delay(1000)));  // still blocked after 1 s
            bad.Close(); // only closing the socket from outside releases it

            using var good = new TcpClient { ReceiveTimeout = 200 };
            await good.ConnectAsync(IPAddress.Loopback, port);
            using var goodPeer = await listener.AcceptTcpClientAsync();
            var sw = Stopwatch.StartNew();
            Assert.Throws<IOException>(() => good.GetStream().Read(new byte[16], 0, 16));
            Assert.True(sw.ElapsedMilliseconds < 1500);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Repro_ESL005_LostSignalBlocksForever()
    {
        var ack = new ManualResetEvent(false); // the acknowledge never comes
        var bad = Task.Run(() => ack.WaitOne()); // flagged by ESL005
        Assert.NotSame(bad, await Task.WhenAny(bad, Task.Delay(500)));

        Assert.False(ack.WaitOne(200)); // fixed: returns false, the caller raises an alarm
        ack.Set();                      // release the stuck task so the test exits cleanly
        await bad;
    }

    [Fact]
    public void Repro_ESL006_CrashMidWriteCorruptsRecipe()
    {
        var dir = Directory.CreateTempSubdirectory("esl006-").FullName;
        try
        {
            var path = Path.Combine(dir, "wafer.rcp");
            var old = "name=wafer;exposure=12.5;gain=2;" + new string('x', 4000);
            var updated = "name=wafer;exposure=13.0;gain=2;" + new string('y', 4000);

            // in place: FileMode.Create truncates first, the process dies after 1 KB
            File.WriteAllText(path, old);
            Assert.Throws<IOException>(() => WriteThenDie(path, updated, 1024));
            Assert.Equal(1024, new FileInfo(path).Length); // neither old nor new recipe survives

            // temp + replace: the crash happens before the swap, the old recipe is intact
            File.WriteAllText(path, old);
            Assert.Throws<IOException>(() => WriteThenDie(path + ".tmp", updated, 1024));
            Assert.Equal(old, File.ReadAllText(path));
            File.WriteAllText(path + ".tmp", updated);
            File.Replace(path + ".tmp", path, null);
            Assert.Equal(updated, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }

        static void WriteThenDie(string target, string text, int bytesBeforeCrash)
        {
            using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
            var bytes = System.Text.Encoding.ASCII.GetBytes(text);
            fs.Write(bytes, 0, bytesBeforeCrash);
            fs.Flush();
            throw new IOException("simulated power loss / kill");
        }
    }

    [Fact]
    public void Repro_ESL007_TornFrameWithoutLock()
    {
        int TornFrames(bool locked)
        {
            var frame = new byte[1 << 20];
            var sync = new object();
            var stop = false;
            var grabber = new Thread(() =>
            {
                byte k = 0;
                while (!Volatile.Read(ref stop))
                {
                    k++;
                    if (locked) lock (sync) Array.Fill(frame, k);
                    else Array.Fill(frame, k); // grabber callback writes the shared buffer
                }
            }) { IsBackground = true };
            grabber.Start();
            var copy = new byte[frame.Length];
            int torn = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 600 && torn == 0)
            {
                if (locked) lock (sync) Buffer.BlockCopy(frame, 0, copy, 0, copy.Length);
                else Buffer.BlockCopy(frame, 0, copy, 0, copy.Length); // UI paint reads it
                if (copy[0] != copy[^1]) torn++;
            }
            Volatile.Write(ref stop, true);
            grabber.Join(1000);
            return torn;
        }

        Assert.True(TornFrames(locked: false) > 0); // screen shows two frames mixed
        Assert.Equal(0, TornFrames(locked: true));
    }

    [Fact]
    public void Repro_ESL008_SlowConsumerGrowsQueue()
    {
        // 100 fps camera, inspection keeps up at 98 fps: simulate one hour in 10 ms steps.
        const int frameBytes = 5 * 1024 * 1024;
        var unbounded = new Queue<int>();
        var bounded = new BlockingCollection<int>(boundedCapacity: 32);
        long dropped = 0;
        double produced = 0, consumed = 0;
        for (var step = 0; step < 360_000; step++)
        {
            produced += 1.00; consumed += 0.98;
            while (produced >= 1) { produced -= 1; unbounded.Enqueue(step); if (!bounded.TryAdd(step)) dropped++; }
            while (consumed >= 1) { consumed -= 1; unbounded.TryDequeue(out _); bounded.TryTake(out _); }
        }
        var unboundedGb = (double)unbounded.Count * frameBytes / (1 << 30);
        Assert.True(unbounded.Count >= 7000);  // ~7,200 frames waiting
        Assert.True(unboundedGb > 30);         // ~35 GB: the vision PC is long dead
        Assert.True(bounded.Count <= 32);      // fixed: memory flat, drops are counted
        Assert.True(dropped > 7000);
    }

    [Fact]
    public async Task Repro_ESL009_InvokeUnderLockDeadlocks()
    {
        // single UI thread with a message queue, like Control.Invoke
        using var ui = new UiThread();
        var sync = new object();

        // worker holds the lock and calls Invoke; meanwhile the UI is painting and wants the same lock
        var workerHasLock = new ManualResetEventSlim();
        var paintQueued = new ManualResetEventSlim();
        var badDone = Task.Run(() =>
        {
            lock (sync)
            {
                workerHasLock.Set();
                paintQueued.Wait();
                ui.Invoke(() => { });                       // flagged by ESL009
            }
        });
        workerHasLock.Wait();
        ui.BeginInvoke(() =>
        {
            // a paint handler that needs the lock; TryEnter with 2 s only so the test can recover
            if (Monitor.TryEnter(sync, 2000)) Monitor.Exit(sync);
        });
        paintQueued.Set();
        Assert.NotSame(badDone, await Task.WhenAny(badDone, Task.Delay(1000))); // both threads stuck: window frozen
        await badDone.WaitAsync(TimeSpan.FromSeconds(5));

        // fixed: copy under the lock, BeginInvoke after releasing it
        var goodDone = Task.Run(() =>
        {
            int copy;
            lock (sync) copy = 42;
            ui.BeginInvoke(() => GC.KeepAlive(copy));
        });
        ui.BeginInvoke(() => { lock (sync) { } });
        Assert.Same(goodDone, await Task.WhenAny(goodDone, Task.Delay(1000)));
    }

    [Fact]
    public void Repro_ESL010_EarlyMessageLostOnRestart()
    {
        int Lost(bool readyFirst)
        {
            var lost = 0;
            for (var restart = 0; restart < 200; restart++)
            {
                var device = new FakeDevice();
                Queue<string>? rx = null;
                var done = new ManualResetEventSlim();
                device.Message += m => { if (Volatile.Read(ref rx) is { } q) lock (q) q.Enqueue(m); else Interlocked.Increment(ref lost); done.Set(); };
                if (readyFirst) Volatile.Write(ref rx, new Queue<string>());
                device.Open(); // first message arrives on the device thread right away
                if (!readyFirst) { Thread.Sleep(1); Volatile.Write(ref rx, new Queue<string>()); } // ~1 ms of setup after Open: flagged by ESL010
                done.Wait(1000);
            }
            return lost;
        }

        Assert.True(Lost(readyFirst: false) > 0); // some restarts drop the first message and the sequence waits forever
        Assert.Equal(0, Lost(readyFirst: true));
    }

    [Fact]
    public async Task Repro_ESL011_StuckSensorHangsSequence()
    {
        var sensor = new StuckSensor(); // cable broken: never reports 'in position'
        var bad = Task.Run(() =>
        {
            while (!sensor.InPosition) // flagged by ESL011
                Thread.Sleep(10);
        });
        Assert.NotSame(bad, await Task.WhenAny(bad, Task.Delay(500)));

        var sw = Stopwatch.StartNew();
        var ex = Assert.Throws<TimeoutException>(() =>
        {
            var deadline = Stopwatch.StartNew();
            while (!sensor.InPosition)
            {
                if (deadline.ElapsedMilliseconds > 200) throw new TimeoutException("axis not in position");
                Thread.Sleep(10);
            }
        });
        Assert.True(sw.ElapsedMilliseconds < 1500);
        sensor.Release();
        await bad;
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int cur;
        while ((cur = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, cur) != cur) { }
    }

    private sealed class FakeDevice
    {
        public event Action<string>? Message;
        public void Open() => ThreadPool.UnsafeQueueUserWorkItem(_ => Message?.Invoke("HELLO"), null);
    }

    private sealed class StuckSensor
    {
        private volatile bool _released;
        public bool InPosition => _released;
        public void Release() => _released = true;
    }

    private sealed class UiThread : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        public UiThread()
        {
            _thread = new Thread(() => { foreach (var a in _queue.GetConsumingEnumerable()) a(); }) { IsBackground = true };
            _thread.Start();
        }
        public void BeginInvoke(Action a) => _queue.Add(a);
        public void Invoke(Action a)
        {
            using var done = new ManualResetEventSlim();
            _queue.Add(() => { a(); done.Set(); });
            done.Wait();
        }
        public void Dispose() { _queue.CompleteAdding(); _thread.Join(3000); }
    }
}
