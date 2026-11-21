using System;
using System.IO.Ports;
using System.Threading;

namespace Sample.Equipment.Bad
{
    // Polls the stage controller every 500 ms and raises an alarm when replies stop.
    public sealed class HeartbeatMonitor : IDisposable
    {
        private readonly SerialPort _port;
        private readonly Timer _timer;
        private int _deadline;
        private DateTime _lastPoll;

        public event Action<string>? Alarm;

        public HeartbeatMonitor(SerialPort port)
        {
            _port = port;
            _port.ReadTimeout = 300;
            _lastPoll = DateTime.Now;
            _timer = new Timer(Poll, null, 0, 500); // expect: ESL003
        }

        private void Poll(object? state)
        {
            var interval = DateTime.Now - _lastPoll; // expect: ESL002
            _lastPoll = DateTime.Now;
            _port.WriteLine("HB?");
            var reply = _port.ReadLine();
            if (reply == "HB OK")
                _deadline = Environment.TickCount + 3000;
            if (Environment.TickCount > _deadline) // expect: ESL001
                Alarm?.Invoke("heartbeat lost, poll interval " + interval.TotalMilliseconds + " ms, uptime "
                              + Environment.TickCount / 1000 + " s"); // expect: ESL001
        }

        public void Dispose() => _timer.Dispose();
    }
}
