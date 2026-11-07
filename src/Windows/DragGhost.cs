using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Glint
{
    // Small click-through thumbnail that follows the cursor during a file drag.
    class DragGhost : Form
    {
        const int FollowMs = 170, EndMs = 130;
        const float FollowAlpha = 225;
        readonly Bitmap thumb;
        readonly Size small;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        RectangleF from, current, endTo;
        float alpha = 255, endAlpha;
        bool ending;
        Action done;

        public DragGhost(Bitmap thumb, Rectangle start, Size small)
        {
            this.thumb = thumb; this.small = small;
            from = current = start;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = start;
            timer.Tick += delegate { Tick(); };
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        // layered, click-through, topmost, tool window, never activated
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80000 | 0x20 | 0x8 | 0x80 | 0x8000000; return c; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Present(); }

        static RectangleF Lerp(RectangleF a, RectangleF b, float t)
        {
            return new RectangleF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);
        }

        public void Tick()
        {
            if (IsDisposed || !IsHandleCreated) return;
            float t = Math.Min(1f, clock.ElapsedMilliseconds / (float)(ending ? EndMs : FollowMs));
            float eased = 1 - (1 - t) * (1 - t) * (1 - t);
            if (ending)
            {
                current = Lerp(from, endTo, eased);
                alpha = FollowAlpha + (endAlpha - FollowAlpha) * eased;
            }
            else
            {
                var p = Cursor.Position;
                current = Lerp(from, new RectangleF(p.X - small.Width / 2f, p.Y - small.Height / 2f, small.Width, small.Height), eased);
                alpha = 255 + (FollowAlpha - 255) * eased;
            }
            Present();
            if (ending && t >= 1) { timer.Stop(); var d = done; done = null; if (d != null) d(); }
        }

        void End(RectangleF to, float toAlpha, Action finished)
        {
            from = current; endTo = to; endAlpha = toAlpha; done = finished; ending = true;
            clock.Restart();
        }

        // Shrinks away where it was dropped.
        public void Drop(Action finished)
        {
            var r = current; r.Inflate(-r.Width * .2f, -r.Height * .2f);
            End(r, 0, finished);
        }

        // Flies back to where the drag started.
        public void Cancel(Rectangle home, Action finished) { End(home, 255, finished); }

        void Present()
        {
            var size = new Size(Math.Max(1, (int)Math.Round(current.Width)), Math.Max(1, (int)Math.Round(current.Height)));
            var pos = new Point((int)Math.Round(current.X), (int)Math.Round(current.Y));
            using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var path = Renderer.Round(new RectangleF(0, 0, size.Width, size.Height), Math.Min(size.Width, size.Height) * .16f))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    using (var tb = new TextureBrush(thumb, WrapMode.Clamp))
                    {
                        tb.ScaleTransform(size.Width / (float)thumb.Width, size.Height / (float)thumb.Height);
                        g.FillPath(tb, path);
                    }
                    using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawPath(pen, path);
                }
                IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), dc = Native.CreateCompatibleDC(IntPtr.Zero), old = Native.SelectObject(dc, hb);
                var src = Point.Empty;
                var blend = new Native.BLENDFUNCTION { Alpha = (byte)Math.Max(0, Math.Min(255, alpha)), Format = 1 };
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, dc, ref src, 0, ref blend, 2);
                Native.SelectObject(dc, old); Native.DeleteObject(hb); Native.DeleteDC(dc);
            }
        }

        protected override void Dispose(bool d) { if (d) timer.Dispose(); base.Dispose(d); }
    }
}
