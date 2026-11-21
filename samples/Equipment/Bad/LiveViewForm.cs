using System;
using System.Drawing;
using System.Windows.Forms;
using Sample.Equipment.Sdk;

namespace Sample.Equipment.Bad
{
    // Live camera view: the grabber thread copies pixels, the UI paints them.
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
            Buffer.BlockCopy(e.Pixels, 0, _image, 0, e.Pixels.Length); // expect: ESL007
            lock (_sync)
            {
                _frameCount++;
                Invoke((Action)UpdateTitle); // expect: ESL009
            }
        }

        private void UpdateTitle()
        {
            lock (_sync) Text = "frames " + _frameCount;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            DrawGray(e.Graphics, _image, 640, 480);
        }

        private static void DrawGray(Graphics g, byte[] pixels, int width, int height) { }
    }
}
