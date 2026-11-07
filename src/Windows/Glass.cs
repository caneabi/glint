using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glint
{
    abstract class Glass : Form
    {
        protected const int S = 16; // shadow margin
        public static bool TestVisible; // test hook
        Bitmap backdrop;
        bool needBackdrop = true;
        int[] rawPixels; long lastHash;
        readonly Timer blurTimer = new Timer { Interval = 33 };
        protected Glass()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            blurTimer.Tick += delegate { BlurTick(); };
        }
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x80000; return c; } }
        protected abstract Rectangle PanelRect { get; }
        public abstract void PaintTo(Graphics g);

        protected void DrawPanel(Graphics g, RectangleF r, float diameter)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var before = g.Save();
            using (var inner = Renderer.Round(r, diameter)) g.SetClip(inner, CombineMode.Exclude);
            for (int i = 10; i > 0; i--)
            {
                var s = r; s.Inflate(i, i); s.Offset(0, 2);
                using (var p = Renderer.Round(s, diameter + i * 2)) using (var b = new SolidBrush(Color.FromArgb(6, 0, 0, 0))) g.FillPath(b, p);
            }
            g.Restore(before);
            using (var p = Renderer.Round(r, diameter))
            {
                if (backdrop != null)
                    using (var tb = new TextureBrush(backdrop, WrapMode.Clamp)) { tb.TranslateTransform(r.X, r.Y); g.FillPath(tb, p); }
                using (var b = new SolidBrush(Color.FromArgb(158, 28, 28, 28))) g.FillPath(b, p);
            }
            var line = r; line.Inflate(-.5f, -.5f);
            using (var p = Renderer.Round(line, diameter - 1)) using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(pen, p);
        }

        // True when the content behind the window changed.
        bool GrabBackdrop()
        {
            needBackdrop = false;
            var pr = PanelRect;
            var sr = new Rectangle(Left + pr.X, Top + pr.Y, pr.Width, pr.Height);
            using (var shot = Imaging.Grab(sr, false))
            {
                var d = shot.LockBits(new Rectangle(0, 0, shot.Width, shot.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int n = shot.Width * shot.Height;
                if (rawPixels == null || rawPixels.Length != n) { rawPixels = new int[n]; }
                Marshal.Copy(d.Scan0, rawPixels, 0, n);
                shot.UnlockBits(d);
                long hash = 17;
                foreach (var v in rawPixels) hash = unchecked(hash * 31 + v);
                if (backdrop != null && hash == lastHash) return false;
                lastHash = hash;
                var result = new Bitmap(pr.Width, pr.Height, PixelFormat.Format32bppArgb);
                using (var small = new Bitmap(Math.Max(1, pr.Width / 12), Math.Max(1, pr.Height / 12), PixelFormat.Format32bppArgb))
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(shot, new Rectangle(0, 0, small.Width, small.Height), 0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel, ia);
                    }
                    using (var g = Graphics.FromImage(result))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(small, new Rectangle(0, 0, result.Width, result.Height), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                if (backdrop != null) backdrop.Dispose();
                backdrop = result;
                return true;
            }
        }

        public void BlurTick()
        {
            if (!IsHandleCreated || !Visible || TestVisible) return;
            if (GrabBackdrop()) Present();
        }
        public Color BackdropCenter { get { return backdrop == null ? Color.Empty : backdrop.GetPixel(backdrop.Width / 2, backdrop.Height / 2); } }

        void Present()
        {
            if (!IsHandleCreated || Width <= 0) return;
            if (needBackdrop) GrabBackdrop();
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) { g.PixelOffsetMode = PixelOffsetMode.Half; PaintTo(g); }
                IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0)), dc = Native.CreateCompatibleDC(IntPtr.Zero), old = Native.SelectObject(dc, hb);
                var pos = Location; var size = Size; var src = Point.Empty;
                var blend = new Native.BLENDFUNCTION { Alpha = 255, Format = 1 };
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, dc, ref src, 0, ref blend, 2);
                Native.SelectObject(dc, old); Native.DeleteObject(hb); Native.DeleteDC(dc);
            }
        }

        public void RefreshBackdrop() { needBackdrop = true; Present(); }

        static readonly List<Glass> Live = new List<Glass>();

        public static void SetCapturable(bool capturable)
        {
            if (Live.Count == 0) return;
            foreach (var w in Live) Native.SetWindowDisplayAffinity(w.Handle, capturable ? 0u : 0x11u);
            if (capturable) { Native.DwmFlush(); Native.DwmFlush(); }
        }

        protected override void OnHandleDestroyed(EventArgs e) { Live.Remove(this); base.OnHandleDestroyed(e); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowDisplayAffinity(Handle, 0x11);
            Live.Add(this);
            Present();
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { RefreshBackdrop(); blurTimer.Start(); } else blurTimer.Stop();
        }
        protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Present(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x232) RefreshBackdrop(); // WM_EXITSIZEMOVE
        }
        protected override void Dispose(bool d) { if (d) { Live.Remove(this); blurTimer.Dispose(); if (backdrop != null) { backdrop.Dispose(); backdrop = null; } } base.Dispose(d); }
    }
}
