using System;

namespace Sample.Equipment.Sdk
{
    // Stand-in for a vendor frame-grabber SDK: frames arrive on the SDK's own thread.
    public sealed class FrameEventArgs : EventArgs
    {
        public FrameEventArgs(byte[] pixels, int width, int height) { Pixels = pixels; Width = width; Height = height; }
        public byte[] Pixels { get; }
        public int Width { get; }
        public int Height { get; }
    }

    public sealed class GrabberDevice
    {
        public event EventHandler<FrameEventArgs>? FrameGrabbed;
        public void StartGrabbing() { }
        public void StopGrabbing() { }
        internal void Raise(byte[] pixels) => FrameGrabbed?.Invoke(this, new FrameEventArgs(pixels, 640, 480));
    }
}
