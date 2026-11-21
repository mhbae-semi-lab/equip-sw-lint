using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Equipment.Bad
{
    // Line-based TCP link to the host (MES) computer.
    public sealed class HostLink
    {
        private readonly AutoResetEvent _ack = new AutoResetEvent(false);
        private TcpClient? _client;
        private StreamReader? _reader;

        public void Connect(string host, int port)
        {
            _client = new TcpClient();
            _client.Connect(host, port);
            _reader = new StreamReader(_client.GetStream(), Encoding.ASCII);
        }

        public string? ReadMessage() => _reader!.ReadLine(); // expect: ESL004

        public async Task<int> ReadBlockAsync(byte[] buffer)
        {
            var stream = _client!.GetStream();
            return await stream.ReadAsync(buffer, 0, buffer.Length); // expect: ESL004
        }

        public void SendAndWaitAck(string message)
        {
            var bytes = Encoding.ASCII.GetBytes(message + "\n");
            _client!.GetStream().Write(bytes, 0, bytes.Length);
            _ack.WaitOne(); // expect: ESL005
        }

        public void OnAckReceived() => _ack.Set();
    }
}
