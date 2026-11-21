using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace Sample.Equipment.Good
{
    // Same heartbeat, fixed: one-shot timer re-armed after each poll, monotonic clock for intervals and deadlines.
    public sealed class HeartbeatMonitor : IDisposable
    {
        private readonly SerialPort _port;
        private readonly Stopwatch _sincePoll = new Stopwatch();
        private readonly Timer _timer;
        private long _deadline;

        public event Action<string>? Alarm;

        public HeartbeatMonitor(SerialPort port)
        {
            _port = port;
            _port.ReadTimeout = 300;
            _deadline = Environment.TickCount64 + 3000;
            _timer = new Timer(Poll, null, 0, Timeout.Infinite);
        }

        private void Poll(object? state)
        {
            try
            {
                var interval = _sincePoll.Elapsed;
                _sincePoll.Restart();
                _port.WriteLine("HB?");
                var reply = _port.ReadLine();
                if (reply == "HB OK")
                    _deadline = Environment.TickCount64 + 3000;
                if (Environment.TickCount64 > _deadline)
                    Alarm?.Invoke("heartbeat lost, poll interval " + interval.TotalMilliseconds + " ms, uptime "
                                  + Environment.TickCount64 / 1000 + " s");
            }
            catch (TimeoutException)
            {
                Alarm?.Invoke("heartbeat reply timeout");
            }
            finally
            {
                _timer.Change(500, Timeout.Infinite);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
