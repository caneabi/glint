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
    class Overlay : Form
    {
        public Action<Tuple<Rectangle, Bitmap>> Done;
        static readonly Color Accent = Color.FromArgb(255, 94, 91);
        readonly bool frozen;
        readonly Bitmap shot, dim; // frozen mode only
        readonly Rectangle virt;
        readonly List<Native.WinInfo> windows;
        bool snap, dragging, moving;
        Point start, cur, moveFrom;
        Rectangle dirty = Rectangle.Empty;
        const int Cells = 15, Cell = 8, Loupe = Cells * Cell, InfoH = 30;
        // live mode
        IntPtr dib, memDc, oldBmp, bits;
        Bitmap buffer;

        public static bool Active;

        public Overlay(bool windowMode)
        {
            var s = Settings.Current;
            frozen = s.Freeze;
            snap = windowMode || s.Snap;
            virt = SystemInformation.VirtualScreen;
            windows = Native.VisibleWindows();
            if (frozen)
            {
                shot = Imaging.Grab(virt);
                dim = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(dim))
                {
                    g.DrawImageUnscaled(shot, 0, 0);
                    using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 0, 0, dim.Width, dim.Height);
                }
            }
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Bounds = virt;
            KeyPreview = true; Cursor = Cursors.Cross;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            cur = new Point(Cursor.Position.X - virt.X, Cursor.Position.Y - virt.Y);
        }

        protected override CreateParams CreateParams
        {
            get { var c = base.CreateParams; if (!frozen) c.ExStyle |= 0x80000; return c; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowPos(Handle, new IntPtr(-1), virt.X, virt.Y, virt.Width, virt.Height, 0x40);
            if (!frozen)
            {
                Native.SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE
                var info = new Native.BITMAPINFO { Size = 40, Width = virt.Width, Height = -virt.Height, Planes = 1, BitCount = 32 };
                memDc = Native.CreateCompatibleDC(IntPtr.Zero);
                dib = Native.CreateDIBSection(memDc, ref info, 0, out bits, IntPtr.Zero, 0);
                oldBmp = Native.SelectObject(memDc, dib);
                buffer = new Bitmap(virt.Width, virt.Height, virt.Width * 4, PixelFormat.Format32bppPArgb, bits);
                Present(new Rectangle(0, 0, virt.Width, virt.Height));
            }
            Activate();
            Native.ForceForeground(Handle);
            Focus();
        }

        void Present(Rectangle clip)
        {
            using (var g = Graphics.FromImage(buffer))
            {
                g.SetClip(clip);
                DrawScene(g, clip);
            }
            var size = new Size(virt.Width, virt.Height); var src = Point.Empty; var pos = virt.Location;
            var blend = new Native.BLENDFUNCTION { Op = 0, Alpha = 255, Format = 1 };
            Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, memDc, ref src, 0, ref blend, 2);
        }

        Rectangle Selection()
        {
            return Rectangle.FromLTRB(Math.Min(start.X, cur.X), Math.Min(start.Y, cur.Y), Math.Max(start.X, cur.X), Math.Max(start.Y, cur.Y));
        }

        Native.WinInfo? HoverInfo()
        {
            var sp = new Point(cur.X + virt.X, cur.Y + virt.Y);
            foreach (var w in windows) if (w.R.Contains(sp)) return w;
            return null;
        }

        Rectangle HoverWindow()
        {
            var i = HoverInfo();
            if (!i.HasValue) return Rectangle.Empty;
            var r = i.Value.R; r.Offset(-virt.X, -virt.Y);
            return r;
        }

        Rectangle Highlight()
        {
            if (dragging) return Selection();
            return snap ? HoverWindow() : Rectangle.Empty;
        }

        Rectangle LoupeRect()
        {
            int x = cur.X + 20, y = cur.Y + 20, h = Loupe + InfoH;
            if (x + Loupe > Width) x = cur.X - Loupe - 20;
            if (y + h > Height) y = cur.Y - h - 20;
            return new Rectangle(x, y, Loupe, h);
        }

        static readonly Font HintFont = new Font("Segoe UI", 8.5f), KeyFont = new Font("Segoe UI Semibold", 8f);
        static readonly Bitmap Measure = new Bitmap(1, 1);
        const int HintH = 34, HintPad = 10, CapH = 20, CapGap = 6, ItemGap = 14;

        // Key, label, whether it's switched on.
        List<Tuple<string, string, bool>> HintItems()
        {
            var items = new List<Tuple<string, string, bool>> {
                Tuple.Create("Esc", L.T("Abbrechen"), false),
                Tuple.Create("W", "Snap", snap),
                Tuple.Create("C", L.T("Fadenkreuz"), Settings.Current.Crosshair),
                Tuple.Create("Tab", L.T("Farbe kopieren"), false) };
            if (dragging) items.Insert(1, Tuple.Create(L.T("Leertaste"), L.T("Verschieben"), moving));
            return items;
        }

        int HintWidth(List<Tuple<string, string, bool>> items)
        {
            using (var g = Graphics.FromImage(Measure))
            {
                float w = HintPad * 2 - ItemGap;
                foreach (var i in items) w += g.MeasureString(i.Item1, KeyFont).Width + 10 + CapGap + g.MeasureString(i.Item2, HintFont).Width + ItemGap;
                return (int)Math.Ceiling(w);
            }
        }

        Rectangle HintRect()
        {
            var b = Screen.FromPoint(new Point(cur.X + virt.X, cur.Y + virt.Y)).Bounds;
            int w = HintWidth(HintItems());
            return new Rectangle(b.X - virt.X + (b.Width - w) / 2, b.Y - virt.Y + 18, w, HintH);
        }

        void Update2()
        {
            var r = Highlight();
            var next = Rectangle.Union(Rectangle.Inflate(LoupeRect(), 2, 2), Rectangle.Inflate(HintRect(), 40, 0)); // width changes with the items
            if (!r.IsEmpty) next = Rectangle.Union(next, Rectangle.Inflate(r, 6, 6));
            if (!r.IsEmpty) next = Rectangle.Union(next, new Rectangle(r.X - 2, r.Y - 34, 190, 34));
            if (Settings.Current.Crosshair)
            {
                next = Rectangle.Union(next, new Rectangle(0, cur.Y - 1, Width, 3));
                next = Rectangle.Union(next, new Rectangle(cur.X - 1, 0, 3, Height));
            }
            var all = dirty.IsEmpty ? next : Rectangle.Union(dirty, next);
            dirty = next;
            if (frozen) Invalidate(all);
            else if (buffer != null) Present(all);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (frozen) DrawScene(e.Graphics, e.ClipRectangle);
        }

        void DrawScene(Graphics g, Rectangle clip)
        {
            var r = Highlight();
            g.CompositingMode = CompositingMode.SourceCopy;
            if (frozen) g.DrawImage(dim, clip, clip, GraphicsUnit.Pixel);
            else using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, clip);
            bool any = r.Width > 0 && r.Height > 0;
            if (any)
            {
                if (frozen) g.DrawImage(shot, r, r, GraphicsUnit.Pixel);
                // Alpha 1 is invisible but still receives mouse input.
                else using (var b = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) g.FillRectangle(b, r);
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if (!dragging && Settings.Current.Crosshair)
                using (var p = new Pen(Color.FromArgb(150, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                {
                    g.DrawLine(p, 0, cur.Y, Width, cur.Y);
                    g.DrawLine(p, cur.X, 0, cur.X, Height);
                }
            if (any) DrawSelection(g, r);
            DrawLoupe(g);
            DrawHint(g);
        }

        void DrawSelection(Graphics g, Rectangle r)
        {
            // Two tones so it shows on light and dark content.
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Color.White)) g.DrawRectangle(p, r);
            using (var p = new Pen(Color.Black) { DashStyle = DashStyle.Custom, DashPattern = new[] { 4f, 4f } }) g.DrawRectangle(p, r);
            string s = r.Width + " × " + r.Height;
            using (var f = new Font("Segoe UI", 8.5f))
            {
                var size = g.MeasureString(s, f);
                float ty = r.Y > 28 ? r.Y - 25 : r.Y + 5, tx = r.X;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Renderer.Round(new RectangleF(tx, ty, size.Width + 8, 20), 10))
                using (var b = new SolidBrush(Color.FromArgb(225, 28, 28, 28))) g.FillPath(b, path);
                g.SmoothingMode = SmoothingMode.None;
                g.DrawString(s, f, Brushes.White, tx + 4, ty + 3);
            }
        }

        void DrawLoupe(Graphics g)
        {
            var r = LoupeRect();
            var dest = new Rectangle(r.X, r.Y, Loupe, Loupe);
            var srcR = new Rectangle(cur.X - Cells / 2, cur.Y - Cells / 2, Cells, Cells);
            Color c = Color.Black;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var panel = Renderer.Round(r, 20))
            {
                using (var b = new SolidBrush(Color.FromArgb(240, 28, 28, 28))) g.FillPath(b, panel);
                var st = g.Save();
                g.SetClip(panel, CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.None;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                if (frozen)
                {
                    g.DrawImage(shot, dest, srcR, GraphicsUnit.Pixel);
                    if (new Rectangle(0, 0, shot.Width, shot.Height).Contains(cur)) c = shot.GetPixel(cur.X, cur.Y);
                }
                else using (var live = Imaging.Grab(new Rectangle(srcR.X + virt.X, srcR.Y + virt.Y, Cells, Cells), false))
                    {
                        g.DrawImage(live, dest, new Rectangle(0, 0, Cells, Cells), GraphicsUnit.Pixel);
                        c = live.GetPixel(Cells / 2, Cells / 2);
                    }
                g.PixelOffsetMode = PixelOffsetMode.Default;
                using (var grid = new Pen(Color.FromArgb(28, 128, 128, 128)))
                    for (int i = 1; i < Cells; i++)
                    {
                        g.DrawLine(grid, dest.X + i * Cell, dest.Y, dest.X + i * Cell, dest.Bottom);
                        g.DrawLine(grid, dest.X, dest.Y + i * Cell, dest.Right, dest.Y + i * Cell);
                    }
                var center = new Rectangle(dest.X + Cells / 2 * Cell, dest.Y + Cells / 2 * Cell, Cell, Cell);
                g.DrawRectangle(Pens.Black, Rectangle.Inflate(center, 1, 1));
                using (var cp = new Pen(Brand.Pixel)) g.DrawRectangle(cp, center);
                g.Restore(st);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var line = new RectangleF(r.X + .5f, r.Y + .5f, r.Width - 1, r.Height - 1);
                using (var lp = Renderer.Round(line, 19)) using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(pen, lp);
            }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, r.X + 9, dest.Bottom + 9, 12, 12);
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawEllipse(p, r.X + 9, dest.Bottom + 9, 12, 12);
            g.SmoothingMode = SmoothingMode.None;
            using (var f = new Font("Segoe UI", 9f)) g.DrawString(Colors.Hex(c), f, Brushes.White, r.X + 26, dest.Bottom + 6);
        }

        void DrawHint(Graphics g)
        {
            var r = HintRect();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Renderer.Round(r, HintH)) using (var b = new SolidBrush(Color.FromArgb(225, 28, 28, 28))) g.FillPath(b, path);
            float x = r.X + HintPad, capY = r.Y + (HintH - CapH) / 2f;
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var left = new StringFormat { LineAlignment = StringAlignment.Center })
                foreach (var i in HintItems())
                {
                    // Lit keycaps show what's switched on.
                    float cw = g.MeasureString(i.Item1, KeyFont).Width + 10;
                    var cap = new RectangleF(x, capY, cw, CapH);
                    using (var path = Renderer.Round(cap, 8)) using (var b = new SolidBrush(i.Item3 ? Accent : Color.FromArgb(68, 68, 74))) g.FillPath(b, path);
                    g.DrawString(i.Item1, KeyFont, i.Item3 ? Brushes.Black : Brushes.White, cap, sf);
                    x += cw + CapGap;
                    float lw = g.MeasureString(i.Item2, HintFont).Width;
                    using (var b = new SolidBrush(Color.FromArgb(200, 200, 205))) g.DrawString(i.Item2, HintFont, b, new RectangleF(x, r.Y, lw + 2, HintH), left);
                    x += lw + ItemGap;
                }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (dragging) { dragging = false; Update2(); } else Close();
                return;
            }
            start = cur = e.Location; dragging = true; Update2();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (moving && dragging)
            {
                start.X += e.X - moveFrom.X; start.Y += e.Y - moveFrom.Y;
                moveFrom = e.Location;
            }
            cur = e.Location; Update2();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            cur = e.Location; dragging = false;
            var r = Selection();
            if (r.Width < 4 || r.Height < 4)
            {
                if (!snap) { Update2(); return; }
                var info = HoverInfo();
                if (!info.HasValue) return;
                if (!info.Value.Desktop) { FinishWindow(info.Value); return; }
                r = HoverWindow();
                r.Intersect(new Rectangle(0, 0, Width, Height));
                if (r.Width < 4) return;
            }
            Finish(r);
        }

        void FinishWindow(Native.WinInfo info)
        {
            Hide();
            var img = Imaging.GrabWindow(info.H);
            Close();
            if (Done != null) Done(Tuple.Create(info.R, img));
        }

        void Finish(Rectangle r)
        {
            var screenR = new Rectangle(r.X + virt.X, r.Y + virt.Y, r.Width, r.Height);
            Bitmap img;
            if (frozen) img = Imaging.Crop(shot, r);
            else { Hide(); Native.DwmFlush(); img = Imaging.Grab(screenR); }
            Close();
            if (Done != null) Done(Tuple.Create(screenR, img));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            else if (e.KeyCode == Keys.W) { snap = !snap; Update2(); }
            else if (e.KeyCode == Keys.C)
            {
                Settings.Current.Crosshair = !Settings.Current.Crosshair;
                Settings.Current.Save();
                dirty = new Rectangle(0, 0, Width, Height);
                Update2();
            }
            else if (e.KeyCode == Keys.Space && dragging && !moving) { moving = true; moveFrom = cur; Update2(); }
        }

        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { moving = false; Update2(); } }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            var code = k & Keys.KeyCode;
            int step = (k & Keys.Shift) != 0 ? 10 : 1;
            if (code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down)
            {
                var c = Cursor.Position;
                Native.SetCursorPos(c.X + (code == Keys.Left ? -step : code == Keys.Right ? step : 0), c.Y + (code == Keys.Up ? -step : code == Keys.Down ? step : 0));
                return true;
            }
            if (code == Keys.Tab)
            {
                Color c;
                var p = new Point(cur.X + virt.X, cur.Y + virt.Y);
                using (var b = frozen ? Imaging.Crop(shot, new Rectangle(cur.X - 10, cur.Y - 10, 21, 21)) : Imaging.Grab(new Rectangle(p.X - 10, p.Y - 10, 21, 21), false))
                    c = (k & Keys.Shift) != 0 ? Imaging.Darkest(b, new Point(b.Width / 2, b.Height / 2)) : b.GetPixel(b.Width / 2, b.Height / 2);
                string t = Colors.Format(c, Settings.Current.ColorFormat);
                Clipboard.SetText(t); Close(); Toast.Show(L.T("Farbe kopiert: ") + t);
                return true;
            }
            return base.ProcessCmdKey(ref m, k);
        }

        public void TestScene(Graphics g, Point from, Point to, Rectangle clip)
        {
            start = from; cur = to; dragging = true;
            g.SetClip(clip);
            DrawScene(g, clip);
            dragging = false;
        }

        public void Simulate(Point a, Point b)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
        }

        protected override void Dispose(bool d)
        {
            if (d)
            {
                Active = false;
                if (shot != null) { shot.Dispose(); dim.Dispose(); }
                if (buffer != null) buffer.Dispose();
                if (memDc != IntPtr.Zero)
                {
                    Native.SelectObject(memDc, oldBmp);
                    Native.DeleteObject(dib);
                    Native.DeleteDC(memDc);
                    memDc = IntPtr.Zero;
                }
            }
            base.Dispose(d);
        }
    }
}
