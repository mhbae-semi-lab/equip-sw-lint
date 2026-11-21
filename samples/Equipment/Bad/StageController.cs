using System.IO.Ports;
using System.Threading;

namespace Sample.Equipment.Bad
{
    // Text protocol over RS-232 to a motion controller.
    public sealed class StageController
    {
        private readonly SerialPort _port = new SerialPort("COM3", 115200);

        public void Open() => _port.Open();

        public string Query(string command)
        {
            _port.WriteLine(command);
            return _port.ReadLine(); // expect: ESL004
        }

        public bool IsInPosition() => Query("INP?") == "1";

        public void MoveAbsolute(double mm)
        {
            _port.WriteLine("MOV " + mm.ToString("F3"));
            while (!IsInPosition()) // expect: ESL011
                Thread.Sleep(10);
        }
    }
}
