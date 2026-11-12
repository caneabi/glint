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
        protected byte Alpha = 255; // whole window, for fade animations
        // Windows 11 rounds normal windows by 8 px, the glass windows match that (as a diameter).
        protected float WindowDiameter { get { return 16f * DeviceDpi / 96f; } }
        static bool Blur { get { return Settings.Current.GlassBlur; } }
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

        Bitmap shadow; string shadowKey; // the soft shadow doesn't change, only the glass on top does

        protected void DrawPanel(Graphics g, RectangleF r, float diameter)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string key = r + "|" + diameter + "|" + Width + "x" + Height;
            if (shadow == null || key != shadowKey)
            {
                if (shadow != null) shadow.Dispose();
                shadow = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb); // same format as the frame, no conversion per draw
                shadowKey = key;
                using (var sg = Graphics.FromImage(shadow))
                {
                    sg.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var inner = Renderer.Round(r, diameter)) sg.SetClip(inner, CombineMode.Exclude);
                    for (int i = 10; i > 0; i--)
                    {
                        var s = r; s.Inflate(i, i); s.Offset(0, 2);
                        using (var p = Renderer.Round(s, diameter + i * 2)) using (var b = new SolidBrush(Color.FromArgb(6, 0, 0, 0))) sg.FillPath(b, p);
                    }
                }
            }
            if (staticPass) { panelRect = r; panelDiameter = diameter; } // glass is mixed in later, per blur step
            else
            {
                g.DrawImageUnscaled(shadow, 0, 0);
                using (var p = Renderer.Round(r, diameter))
                {
                    if (!Blur) { using (var b = new SolidBrush(Color.FromArgb(255, 34, 34, 37))) g.FillPath(b, p); }
                    else if (backdrop != null)
                        using (var tb = new TextureBrush(backdrop, WrapMode.Clamp))
                        {
                            tb.TranslateTransform(r.X, r.Y);
                            tb.ScaleTransform(r.Width / backdrop.Width, r.Height / backdrop.Height);
                            var mode = g.InterpolationMode;
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.FillPath(tb, p);
                            g.InterpolationMode = mode;
                        }
                    if (Blur) using (var b = new SolidBrush(Color.FromArgb(158, 28, 28, 28))) g.FillPath(b, p);
                }
            }
            if (staticPass) g.DrawImageUnscaled(shadow, 0, 0);
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
                var small = new Bitmap(Math.Max(1, pr.Width / 12), Math.Max(1, pr.Height / 12), PixelFormat.Format32bppArgb);
                using (var ia = new ImageAttributes())
                using (var g = Graphics.FromImage(small))
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(shot, new Rectangle(0, 0, small.Width, small.Height), 0, 0, shot.Width, shot.Height, GraphicsUnit.Pixel, ia);
                }
                if (backdrop != null) backdrop.Dispose();
                backdrop = small;
                return true;
            }
        }

        public void BlurTick()
        {
            if (!IsHandleCreated || !Visible || TestVisible || !Blur) return;
            if (GrabBackdrop()) Present();
        }
        public Color BackdropCenter { get { return backdrop == null ? Color.Empty : backdrop.GetPixel(backdrop.Width / 2, backdrop.Height / 2); } }

        void Present()
        {
            if (!IsHandleCreated || Width <= 0) return;
            if (needBackdrop && Blur) GrabBackdrop();
            EnsureFrame();
            if (staticDirty || layer == null || layer.Width != Width || layer.Height != Height) BuildLayer();
            MixGlass();
            var pos = Location; var size = Size; var src = Point.Empty;
            var blend = new Native.BLENDFUNCTION { Alpha = Alpha, Format = 1 };
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, frameDc, ref src, 0, ref blend, 2);
        }

        // Everything but the glass: shadow, border and content. Rebuilt only when the window repaints, not per blur step.
        bool staticPass, staticDirty = true;
        RectangleF panelRect; float panelDiameter;
        Bitmap layer, glass;
        int[] layerPx, glassPx, framePx;
        byte[] mask; string maskKey;

        void BuildLayer()
        {
            if (layer == null || layer.Width != Width || layer.Height != Height)
            {
                if (layer != null) layer.Dispose();
                layer = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            using (var g = Graphics.FromImage(layer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                staticPass = true;
                try { PaintTo(g); } finally { staticPass = false; }
            }
            layerPx = Pixels(layer, layerPx);
            string key = panelRect + "|" + panelDiameter + "|" + Width + "x" + Height;
            if (key != maskKey)
            {
                // Antialiased coverage of the rounded panel, one byte per pixel.
                using (var m = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(m))
                    using (var p = Renderer.Round(panelRect, panelDiameter))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.FillPath(Brushes.White, p);
                    }
                    var px = Pixels(m, null);
                    mask = new byte[px.Length];
                    for (int i = 0; i < px.Length; i++) mask[i] = (byte)((uint)px[i] >> 24);
                }
                maskKey = key;
            }
            staticDirty = false;
        }

        static int[] Pixels(Bitmap b, int[] into)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, b.PixelFormat);
            int n = b.Width * b.Height;
            if (into == null || into.Length != n) into = new int[n];
            Marshal.Copy(d.Scan0, into, 0, n);
            b.UnlockBits(d);
            return into;
        }

        // Puts the glass under the cached layer straight in the frame buffer, much faster than GDI+ for big windows.
        void MixGlass()
        {
            int n = Width * Height;
            if (framePx == null || framePx.Length != n) framePx = new int[n];
            Array.Copy(layerPx, framePx, n);
            var body = Rectangle.Intersect(Rectangle.Round(panelRect), new Rectangle(0, 0, Width, Height));
            if (body.Width > 0 && body.Height > 0)
            {
                bool blur = Blur && backdrop != null;
                if (blur)
                {
                    if (glass == null || glass.Size != body.Size) { if (glass != null) glass.Dispose(); glass = new Bitmap(body.Width, body.Height, PixelFormat.Format32bppArgb); }
                    using (var g = Graphics.FromImage(glass))
                    using (var ia = new ImageAttributes())
                    {
                        ia.SetWrapMode(WrapMode.TileFlipXY);
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(backdrop, new Rectangle(0, 0, body.Width, body.Height), 0, 0, backdrop.Width, backdrop.Height, GraphicsUnit.Pixel, ia);
                    }
                    glassPx = Pixels(glass, glassPx);
                }
                for (int y = 0; y < body.Height; y++)
                {
                    int row = (y + body.Y) * Width + body.X, grow = y * body.Width;
                    for (int x = 0; x < body.Width; x++)
                    {
                        int i = row + x, m = mask[i];
                        if (m == 0) continue;
                        int o = framePx[i], oa = (int)((uint)o >> 24);
                        if (oa == 255) continue;
                        int r, gr, b;
                        if (blur)
                        {
                            int c = glassPx[grow + x];
                            // dark tint over the blurred backdrop, like the 158 alpha fill
                            r = (((c >> 16) & 255) * 97 + 28 * 158) / 255; gr = (((c >> 8) & 255) * 97 + 28 * 158) / 255; b = ((c & 255) * 97 + 28 * 158) / 255;
                        }
                        else { r = 34; gr = 34; b = 37; }
                        int inv = 255 - oa;
                        int a = oa + m * inv / 255;
                        r = ((o >> 16) & 255) + r * m / 255 * inv / 255;
                        gr = ((o >> 8) & 255) + gr * m / 255 * inv / 255;
                        b = (o & 255) + b * m / 255 * inv / 255;
                        framePx[i] = a << 24 | r << 16 | gr << 8 | b;
                    }
                }
            }
            Marshal.Copy(framePx, 0, frameBits, n);
        }

        // One lasting premultiplied buffer that Windows reads directly, instead of a new bitmap per frame.
        IntPtr frameDc, frameDib, frameOld, frameBits;
        Bitmap frame;

        void EnsureFrame()
        {
            if (frame != null && frame.Width == Width && frame.Height == Height) return;
            FreeFrame();
            var info = new Native.BITMAPINFO { Size = 40, Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
            frameDc = Native.CreateCompatibleDC(IntPtr.Zero);
            frameDib = Native.CreateDIBSection(frameDc, ref info, 0, out frameBits, IntPtr.Zero, 0);
            frameOld = Native.SelectObject(frameDc, frameDib);
            frame = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, frameBits);
        }

        void FreeFrame()
        {
            if (frame != null) { frame.Dispose(); frame = null; }
            if (frameDc != IntPtr.Zero) { Native.SelectObject(frameDc, frameOld); Native.DeleteObject(frameDib); Native.DeleteDC(frameDc); frameDc = IntPtr.Zero; }
        }

        public void RefreshBackdrop() { needBackdrop = true; Present(); }

        // Test hook: ms for grabbing the backdrop and for presenting, each.
        public double[] TestCost(int n)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < n; i++) { lastHash = 0; GrabBackdrop(); }
            double grab = sw.Elapsed.TotalMilliseconds / n;
            sw.Restart();
            for (int i = 0; i < n; i++) Present();
            return new[] { grab, sw.Elapsed.TotalMilliseconds / n };
        }

        // After the blur setting changed.
        public static void ApplyBlurSetting()
        {
            foreach (var w in Live.ToArray())
            {
                if (Blur && w.Visible) w.blurTimer.Start(); else w.blurTimer.Stop();
                w.RefreshBackdrop();
            }
        }

        static readonly List<Glass> Live = new List<Glass>();

        public static void SetCapturable(bool capturable)
        {
            if (Live.Count == 0) return;
            foreach (var w in Live) Native.SetWindowDisplayAffinity(w.Handle, capturable ? 0u : 0x11u);
            if (capturable) { Native.DwmFlush(); Native.DwmFlush(); }
        }

        protected override void OnHandleDestroyed(EventArgs e) { Live.Remove(this); FreeFrame(); base.OnHandleDestroyed(e); }

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
            if (Visible) { RefreshBackdrop(); if (Blur) blurTimer.Start(); } else blurTimer.Stop();
        }
        protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); staticDirty = true; Present(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x232) RefreshBackdrop(); // WM_EXITSIZEMOVE
        }
        protected override void Dispose(bool d) { if (d) { Live.Remove(this); blurTimer.Dispose(); if (shadow != null) { shadow.Dispose(); shadow = null; } FreeFrame(); if (layer != null) layer.Dispose(); if (glass != null) glass.Dispose(); if (backdrop != null) { backdrop.Dispose(); backdrop = null; } } base.Dispose(d); }
    }
}
