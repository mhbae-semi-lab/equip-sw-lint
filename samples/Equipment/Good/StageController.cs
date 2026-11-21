using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;

namespace Sample.Equipment.Good
{
    // Same controller, fixed: read timeout on the port, motion wait with a deadline and an alarm.
    public sealed class StageController
    {
        private readonly SerialPort _port = new SerialPort("COM3", 115200) { ReadTimeout = 500, WriteTimeout = 500 };

        public void Open() => _port.Open();

        public string Query(string command)
        {
            _port.WriteLine(command);
            return _port.ReadLine();
        }

        public bool IsInPosition() => Query("INP?") == "1";

        public void MoveAbsolute(double mm, int timeoutMs = 5000)
        {
            _port.WriteLine("MOV " + mm.ToString("F3"));
            var sw = Stopwatch.StartNew();
            while (!IsInPosition())
            {
                if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("stage did not reach " + mm + " mm");
                Thread.Sleep(10);
            }
        }
    }
}
