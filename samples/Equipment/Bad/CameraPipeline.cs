using System.Collections.Concurrent;
using System.Threading;
using Sample.Equipment.Sdk;

namespace Sample.Equipment.Bad
{
    // Grabber callback queues frames; a worker thread inspects them.
    public sealed class CameraPipeline
    {
        private readonly GrabberDevice _camera;
        private ConcurrentQueue<byte[]>? _frames;
        private Thread? _worker;
        private volatile bool _running;

        public CameraPipeline(GrabberDevice camera) { _camera = camera; }

        public void Start()
        {
            _camera.FrameGrabbed += OnFrameGrabbed;
            _camera.StartGrabbing();
            _frames = new ConcurrentQueue<byte[]>(); // expect: ESL010
            _running = true;
            _worker = new Thread(InspectLoop) { IsBackground = true };
            _worker.Start();
        }

        public void Stop()
        {
            _running = false;
            _camera.StopGrabbing();
            _worker?.Join(); // expect: ESL005
        }

        private void OnFrameGrabbed(object? sender, FrameEventArgs e)
        {
            _frames!.Enqueue(e.Pixels); // expect: ESL008
        }

        private void InspectLoop()
        {
            while (_running)
            {
                if (_frames != null && _frames.TryDequeue(out var frame)) Inspect(frame);
                else Thread.Sleep(1);
            }
        }

        private static void Inspect(byte[] frame) { }
    }
}
