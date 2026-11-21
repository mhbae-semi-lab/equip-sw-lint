using System;
using System.Drawing;
using System.Windows.Forms;
using Sample.Equipment.Sdk;

namespace Sample.Equipment.Good
{
    // Same live view, fixed: both sides lock the image, the UI update is posted after the lock is released.
    public sealed class LiveViewForm : Form
    {
        private readonly GrabberDevice _camera;
        private readonly byte[] _image = new byte[640 * 480];
        private readonly object _sync = new object();
        private int _frameCount;

        public LiveViewForm(GrabberDevice camera)
        {
            _camera = camera;
            _camera.FrameGrabbed += OnFrameGrabbed;
        }

        private void OnFrameGrabbed(object? sender, FrameEventArgs e)
        {
            int count;
            lock (_sync)
            {
                Buffer.BlockCopy(e.Pixels, 0, _image, 0, e.Pixels.Length);
                count = ++_frameCount;
            }
            BeginInvoke((Action)(() => Text = "frames " + count));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            lock (_sync) DrawGray(e.Graphics, _image, 640, 480);
        }

        private static void DrawGray(Graphics g, byte[] pixels, int width, int height) { }
    }
}
