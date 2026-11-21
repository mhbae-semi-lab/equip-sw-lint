using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Equipment.Good
{
    // Same host link, fixed: receive timeout, cancellable async read, bounded ack wait.
    public sealed class HostLink
    {
        private readonly AutoResetEvent _ack = new AutoResetEvent(false);
        private TcpClient? _client;
        private StreamReader? _reader;

        public void Connect(string host, int port)
        {
            _client = new TcpClient { ReceiveTimeout = 2000, SendTimeout = 2000 };
            _client.Connect(host, port);
            _reader = new StreamReader(_client.GetStream(), Encoding.ASCII);
        }

        public string? ReadMessage() => _reader!.ReadLine();

        public async Task<int> ReadBlockAsync(byte[] buffer)
        {
            var stream = _client!.GetStream();
            using var cts = new CancellationTokenSource(2000);
            return await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
        }

        public void SendAndWaitAck(string message)
        {
            var bytes = Encoding.ASCII.GetBytes(message + "\n");
            _client!.GetStream().Write(bytes, 0, bytes.Length);
            if (!_ack.WaitOne(3000)) throw new TimeoutException("host did not acknowledge");
        }

        public void OnAckReceived() => _ack.Set();
    }
}
