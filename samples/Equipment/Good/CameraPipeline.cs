using System.Collections.Concurrent;
using System.Threading;
using Sample.Equipment.Sdk;

namespace Sample.Equipment.Good
{
    // Same pipeline, fixed: everything the callback needs exists before grabbing starts; the queue is bounded.
    public sealed class CameraPipeline
    {
        private readonly GrabberDevice _camera;
        private BlockingCollection<byte[]>? _frames;
        private Thread? _worker;
        private volatile bool _running;
        private long _dropped;

        public CameraPipeline(GrabberDevice camera) { _camera = camera; }

        public long Dropped => Interlocked.Read(ref _dropped);

        public void Start()
        {
            _frames = new BlockingCollection<byte[]>(boundedCapacity: 32);
            _running = true;
            _worker = new Thread(InspectLoop) { IsBackground = true };
            _worker.Start();
            _camera.FrameGrabbed += OnFrameGrabbed;
            _camera.StartGrabbing();
        }

        public void Stop()
        {
            _camera.StopGrabbing();
            _running = false;
            if (_worker != null && !_worker.Join(2000)) throw new System.TimeoutException("inspection thread did not stop");
        }

        private void OnFrameGrabbed(object? sender, FrameEventArgs e)
        {
            if (!_frames!.TryAdd(e.Pixels)) Interlocked.Increment(ref _dropped);
        }

        private void InspectLoop()
        {
            while (_running)
            {
                if (_frames!.TryTake(out var frame, 100)) Inspect(frame);
            }
        }

        private static void Inspect(byte[] frame) { }
    }
}
