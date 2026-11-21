using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace EquipSwLint.Tests;

/// <summary>One positive and one negative case per rule. {|ESLxxx:...|} marks the expected span.</summary>
public class AnalyzerTests
{
    // .NET Framework 4.8 + WinForms/WPF: the usual target of equipment PCs
    private static readonly ReferenceAssemblies NetFx = ReferenceAssemblies.NetFramework.Net48.Default
        .AddAssemblies(ImmutableArray.Create("System.Xml.Linq", "System.Windows.Forms", "System.Drawing", "PresentationFramework", "PresentationCore", "WindowsBase"));

    private static readonly ReferenceAssemblies Net8 = ReferenceAssemblies.Net.Net80;

    private static Task Verify<T>(string code, ReferenceAssemblies? refs = null) where T : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<T, DefaultVerifier> { TestCode = code, ReferenceAssemblies = refs ?? NetFx };
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        return test.RunAsync();
    }

    // ESL001
    [Fact]
    public Task TickCount_DeadlineAndCompare_Reported() => Verify<TickCountAnalyzer>("""
        using System;
        class C {
            int _deadline;
            void Arm() { _deadline = Environment.TickCount + 3000; }
            bool Expired() => {|ESL001:Environment.TickCount|} > _deadline;
            long Widened() { long t = {|ESL001:Environment.TickCount|}; return t; }
            double UptimeSeconds() => {|ESL001:Environment.TickCount|} / 1000.0;
        }
        """);

    [Fact]
    public Task TickCount_PInvoke_Reported() => Verify<TickCountAnalyzer>("""
        using System.Runtime.InteropServices;
        class C {
            [DllImport("kernel32")] static extern uint GetTickCount();
            uint _deadline;
            bool Expired() => {|ESL001:GetTickCount()|} > _deadline;
        }
        """);

    [Fact]
    public Task TickCount_WrapSafeDifferenceAndSeed_NotReported() => Verify<TickCountAnalyzer>("""
        using System;
        class C {
            int _start = Environment.TickCount;
            bool Expired(int timeout) => Environment.TickCount - _start > timeout;
            Random R() => new Random(Environment.TickCount);
            bool Same(int t) => t == Environment.TickCount;
            int _next;
            void Arm() { _next = Environment.TickCount + 5000; }
            bool Due() => _next - Environment.TickCount <= 0; // deadline compared by difference: wrap-safe
        }
        """);

    // ESL002
    [Fact]
    public Task WallClock_IntervalAndDeadline_Reported() => Verify<WallClockAnalyzer>("""
        using System;
        class C {
            DateTime _last;
            void Mark() { _last = DateTime.Now; }
            double Ms() => ({|ESL002:DateTime.Now - _last|}).TotalMilliseconds;
            void Wait() {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while ({|ESL002:DateTime.UtcNow < deadline|}) { }
            }
        }
        """);

    [Fact]
    public Task WallClock_RetentionAndTimestamps_NotReported() => Verify<WallClockAnalyzer>("""
        using System;
        using System.IO;
        class C {
            bool Old(string f) => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30);
            string Stamp() => DateTime.Now.ToString("HH:mm:ss");
            TimeSpan Age(DateTime created) => DateTime.Now - created;
        }
        """);

    // ESL003
    [Fact]
    public Task Timer_BlockingCallbackWithoutGuard_Reported() => Verify<TimerAnalyzer>("""
        using System.IO.Ports;
        using System.Threading;
        class C {
            SerialPort _port = new SerialPort("COM1");
            Timer _t;
            System.Timers.Timer _t2 = new System.Timers.Timer(100);
            C() {
                _t = {|ESL003:new Timer(Poll, null, 0, 200)|};
                {|ESL003:_t2.Elapsed += (s, e) => Thread.Sleep(500)|};
            }
            void Poll(object state) { _port.WriteLine("?"); _port.ReadLine(); }
        }
        """);

    [Fact]
    public Task Timer_OneShotOrGuardedOrTrivial_NotReported() => Verify<TimerAnalyzer>("""
        using System.IO.Ports;
        using System.Threading;
        class C {
            SerialPort _port = new SerialPort("COM1");
            Timer _a, _b, _c;
            int _busy, _ticks;
            C() {
                _a = new Timer(OneShot, null, 0, Timeout.Infinite);
                _b = new Timer(Guarded, null, 0, 200);
                _c = new Timer(_ => _ticks++, null, 0, 200);
            }
            void OneShot(object s) { _port.ReadLine(); _a.Change(200, Timeout.Infinite); }
            void Guarded(object s) {
                if (Interlocked.Exchange(ref _busy, 1) == 1) return;
                try { _port.ReadLine(); } finally { _busy = 0; }
            }
        }
        """);

    // ESL004
    [Fact]
    public Task CommRead_NoTimeout_Reported() => Verify<CommReadAnalyzer>("""
        using System.IO;
        using System.IO.Ports;
        using System.Net.Sockets;
        class C {
            SerialPort _port = new SerialPort("COM1");
            TcpClient _client = new TcpClient();
            string Serial() => {|ESL004:_port.ReadLine()|};
            string Tcp() { var r = new StreamReader(_client.GetStream()); return {|ESL004:r.ReadLine()|}; }
            int Raw(byte[] b) { NetworkStream s = _client.GetStream(); return {|ESL004:s.Read(b, 0, b.Length)|}; }
        }
        """);

    [Fact]
    public Task CommRead_UdpListenerAndSocketEventArgs_NotReported() => Verify<CommReadAnalyzer>("""
        using System.Net;
        using System.Net.Sockets;
        class C {
            UdpClient _udp = new UdpClient(10110);
            Socket _s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            byte[] Listen() { var ep = new IPEndPoint(IPAddress.Any, 0); return _udp.Receive(ref ep); }
            bool Pump(SocketAsyncEventArgs e) => _s.ReceiveAsync(e);
        }
        """);

    [Fact]
    public Task CommRead_AsyncWithoutToken_ReportedEvenWithTimeout() => Verify<CommReadAnalyzer>("""
        using System.Net.Sockets;
        using System.Threading.Tasks;
        class C {
            TcpClient _client = new TcpClient { ReceiveTimeout = 1000 };
            Task<int> Read(byte[] b) => {|ESL004:_client.GetStream().ReadAsync(b, 0, b.Length)|};
        }
        """);

    [Fact]
    public Task CommRead_TimeoutOrTokenOrAvailable_NotReported() => Verify<CommReadAnalyzer>("""
        using System.IO.Ports;
        using System.Net.Sockets;
        using System.Threading;
        using System.Threading.Tasks;
        class A { SerialPort _port = new SerialPort("COM1") { ReadTimeout = 500 }; string R() => _port.ReadLine(); }
        class B {
            SerialPort _port = new SerialPort("COM1");
            string R() { if (_port.BytesToRead > 0) return _port.ReadExisting(); return ""; }
            int One() { if (_port.BytesToRead > 0) return _port.ReadByte(); return -1; }
        }
        class D {
            TcpClient _client = new TcpClient();
            Task<int> R(byte[] b, CancellationToken ct) => _client.GetStream().ReadAsync(b, 0, b.Length, ct);
        }
        """);

    // ESL005
    [Fact]
    public Task UntimedWait_Reported() => Verify<UntimedWaitAnalyzer>("""
        using System.Diagnostics;
        using System.Threading;
        class C {
            AutoResetEvent _e = new AutoResetEvent(false);
            SemaphoreSlim _s = new SemaphoreSlim(1);
            void M(Thread t, Process p, object gate) {
                {|ESL005:_e.WaitOne()|};
                {|ESL005:t.Join()|};
                {|ESL005:_s.Wait()|};
                {|ESL005:p.WaitForExit()|};
                lock (gate) {|ESL005:Monitor.Wait(gate)|};
            }
        }
        """);

    [Fact]
    public Task UntimedWait_TimeoutOrToken_NotReported() => Verify<UntimedWaitAnalyzer>("""
        using System.Threading;
        class C {
            AutoResetEvent _e = new AutoResetEvent(false);
            SemaphoreSlim _s = new SemaphoreSlim(1);
            void M(Thread t, CancellationToken ct) {
                _e.WaitOne(1000);
                t.Join(2000);
                _s.Wait(ct);
                _s.Wait(System.TimeSpan.FromSeconds(1));
                ct.WaitHandle.WaitOne();
                WaitHandle.WaitAny(new[] { ct.WaitHandle, _e });
            }
        }
        """);

    // ESL006
    [Fact]
    public Task NonAtomicWrite_Reported() => Verify<NonAtomicWriteAnalyzer>("""
        using System.IO;
        using System.Xml.Linq;
        class C {
            void A(string recipePath, string text) => {|ESL006:File.WriteAllText(recipePath, text)|};
            void B(XDocument d) => {|ESL006:d.Save(@"C:\Machine\config.xml")|};
            void E(string dir) { using var w = {|ESL006:new StreamWriter(Path.Combine(dir, "settings.ini"))|}; }
            void F(string calibFile) { using var s = {|ESL006:new FileStream(calibFile, FileMode.Create)|}; }
        }
        """);

    [Fact]
    public Task NonAtomicWrite_TempRenameLogsAppend_NotReported() => Verify<NonAtomicWriteAnalyzer>("""
        using System.IO;
        class C {
            void Save(string recipePath, string text) {
                var tmp = recipePath + ".tmp";
                File.WriteAllText(tmp, text);
                File.Replace(tmp, recipePath, null);
            }
            void Log(string text) => File.WriteAllText("run.log", text);
            void Append(string configPath, string line) { using var w = new StreamWriter(configPath, append: true); w.WriteLine(line); }
            void Read(string recipePath) { using var s = new FileStream(recipePath, FileMode.Open); }
        }
        """);

    // ESL007
    [Fact]
    public Task SharedBuffer_CallbackWritesUiReads_Reported() => Verify<ThreadContextAnalyzer>("""
        using System;
        using System.Threading.Tasks;
        using System.Windows.Forms;
        class FrameArgs : EventArgs { public byte[] Pixels; }
        class Camera { public event EventHandler<FrameArgs> FrameGrabbed; }
        class View : Form {
            byte[] _image = new byte[16];
            int[] _profile = new int[4];
            View(Camera c) { c.FrameGrabbed += OnFrame; }
            void OnFrame(object s, FrameArgs e) { Array.Copy(e.Pixels, {|ESL007:_image|}, 16); }
            protected override void OnPaint(PaintEventArgs e) { Draw(_image); }
            void button1_Click(object s, EventArgs e) { Task.Run(() => { {|ESL007:_profile|}[0] = 1; }); Show(_profile); }
            static void Draw(byte[] b) { }
            static void Show(int[] p) { }
        }
        """);

    [Fact]
    public Task SharedBuffer_LockedOrSingleThread_NotReported() => Verify<ThreadContextAnalyzer>("""
        using System;
        using System.Windows.Forms;
        class FrameArgs : EventArgs { public byte[] Pixels; }
        class Camera { public event EventHandler<FrameArgs> FrameGrabbed; }
        class View : Form {
            readonly object _sync = new object();
            byte[] _image = new byte[16];
            byte[] _lut = new byte[256];
            View(Camera c) { c.FrameGrabbed += OnFrame; }
            void OnFrame(object s, FrameArgs e) { lock (_sync) Array.Copy(e.Pixels, _image, 16); }
            protected override void OnPaint(PaintEventArgs e) { lock (_sync) Draw(_image); _lut[0] = 1; Draw(_lut); }
            static void Draw(byte[] b) { }
        }
        """);

    // ESL008
    [Fact]
    public Task UnboundedQueue_FromCallback_Reported() => Verify<ThreadContextAnalyzer>("""
        using System;
        using System.Collections.Concurrent;
        using System.IO.Ports;
        class C {
            readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();
            readonly BlockingCollection<byte[]> _frames = new BlockingCollection<byte[]>();
            C(SerialPort p) { p.DataReceived += OnData; }
            void OnData(object s, SerialDataReceivedEventArgs e) {
                {|ESL008:_lines.Enqueue("x")|};
                {|ESL008:_frames.Add(new byte[1])|};
            }
        }
        """);

    [Fact]
    public Task UnboundedQueue_BoundedOrChecked_NotReported() => Verify<ThreadContextAnalyzer>("""
        using System;
        using System.Collections.Concurrent;
        using System.IO.Ports;
        class C {
            readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();
            readonly BlockingCollection<byte[]> _frames = new BlockingCollection<byte[]>(boundedCapacity: 8);
            readonly ConcurrentQueue<int> _ui = new ConcurrentQueue<int>();
            C(SerialPort p) { p.DataReceived += OnData; }
            void OnData(object s, SerialDataReceivedEventArgs e) {
                if (_lines.Count > 1000) _lines.TryDequeue(out _);
                _lines.Enqueue("x");
                _frames.TryAdd(new byte[1]);
            }
            void Button() { _ui.Enqueue(1); }
        }
        """);

    // ESL009
    [Fact]
    public Task InvokeUnderLock_Reported() => Verify<InvokeUnderLockAnalyzer>("""
        using System;
        using System.Windows.Forms;
        class C : Form {
            readonly object _sync = new object();
            void Worker() {
                lock (_sync) { {|ESL009:Invoke((Action)(() => Text = "x"))|}; }
                lock (_sync) { {|ESL009:System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { })|}; }
            }
        }
        """);

    [Fact]
    public Task InvokeUnderLock_BeginInvokeOrOutsideLock_NotReported() => Verify<InvokeUnderLockAnalyzer>("""
        using System;
        using System.Windows.Forms;
        class C : Form {
            readonly object _sync = new object();
            string _s;
            void Worker() {
                string copy;
                lock (_sync) { copy = _s; BeginInvoke((Action)(() => Text = copy)); }
                Invoke((Action)(() => Text = copy));
                lock (_sync) { Action later = () => Invoke((Action)(() => { })); }
            }
        }
        """);

    // ESL010
    [Fact]
    public Task StartBeforeReady_Reported() => Verify<ThreadContextAnalyzer>("""
        using System.IO.Ports;
        using System.Text;
        using System.Threading;
        class C {
            SerialPort _port = new SerialPort("COM1");
            StringBuilder _rx;
            Thread _t;
            object _state;
            void Start() {
                _port.DataReceived += OnData;
                _port.Open();
                {|ESL010:_rx = new StringBuilder()|};
            }
            void StartLoop() {
                _t = new Thread(Loop);
                _t.Start();
                {|ESL010:_state = new object()|};
            }
            void OnData(object s, SerialDataReceivedEventArgs e) { lock (_rx) _rx.Append(_port.ReadExisting()); }
            void Loop() { while (_state == null) { } }
        }
        """);

    [Fact]
    public Task StartBeforeReady_ReadyFirst_NotReported() => Verify<ThreadContextAnalyzer>("""
        using System.IO.Ports;
        using System.Text;
        class C {
            SerialPort _port = new SerialPort("COM1");
            StringBuilder _rx;
            int _opened;
            void Start() {
                _rx = new StringBuilder();
                _port.DataReceived += OnData;
                _port.Open();
                _opened = 1;
            }
            void OnData(object s, SerialDataReceivedEventArgs e) { lock (_rx) _rx.Append(_port.ReadExisting()); }
        }
        """);

    // ESL011
    [Fact]
    public Task PollWithoutDeadline_Reported() => Verify<PollWithoutDeadlineAnalyzer>("""
        using System.Threading;
        using System.Threading.Tasks;
        class Io { public bool Get(int bit) => true; }
        class C {
            Io _io = new Io();
            bool _done;
            void A() { {|ESL011:while|} (!_io.Get(5)) Thread.Sleep(10); }
            async Task B() { {|ESL011:while|} (!_done) { await Task.Delay(5); } }
            void D() { {|ESL011:do|} { Thread.Sleep(1); } while (!_io.Get(3)); }
        }
        """);

    [Fact]
    public Task PollWithoutDeadline_DeadlineOrWork_NotReported() => Verify<PollWithoutDeadlineAnalyzer>("""
        using System.Diagnostics;
        using System.Threading;
        class Io { public bool Get(int bit) => true; }
        class C {
            Io _io = new Io();
            volatile bool _stopRequested;
            void A() { var sw = Stopwatch.StartNew(); while (!_io.Get(5) && sw.ElapsedMilliseconds < 3000) Thread.Sleep(10); }
            void B() { int retry = 0; while (!_io.Get(5) && retry++ < 100) Thread.Sleep(10); }
            void D() { while (!_stopRequested) Thread.Sleep(100); }
            void G(long target) { do { Thread.SpinWait(1); } while (Stopwatch.GetTimestamp() < target); }
            bool _terminated;
            void H() { while (!_terminated) Thread.Sleep(100); }
            void E() { while (!_io.Get(1)) { Work(); Thread.Sleep(1); } }
            void F() { for (int i = 0; i < 3; i++) Thread.Sleep(1); foreach (var x in new[] { 1 }) Thread.Sleep(x); }
            void Work() { }
        }
        """);

    // descriptor sanity: every rule has ko/en text and a repro test
    [Fact]
    public void Rules_HaveTextAndRepro()
    {
        foreach (var r in Rules.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.TitleKo + r.WhyKo + r.FixKo + r.MessageKo), r.Id);
            Assert.NotNull(typeof(ReproTests).GetMethod(r.Repro));
        }
        Assert.Equal(11, Rules.All.Length);
    }
}
