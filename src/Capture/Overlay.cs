using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glint
{
    class Overlay : Form
    {
        public Action<Tuple<Rectangle, Bitmap>> Done;
        static readonly Color Accent = Color.FromArgb(255, 94, 91);
        readonly bool frozen;
        readonly bool pickOnly; // opened as color picker, not from snipping
        bool picker; // round loupe, Tab toggles it while snipping
        bool cursorHidden;
        readonly Bitmap shot, dim; // frozen mode only
        readonly Rectangle virt;
        readonly List<Native.WinInfo> windows;
        bool snap, dragging, moving;
        Point start, cur, moveFrom;
        // What the last frame drew, so the next one only repaints what moved.
        bool redrawAll = true;
        Rectangle lastLoupe, lastHint, lastHighlight;
        Point lastCur;
        const int Cells = 15, Cell = 8, Loupe = Cells * Cell, InfoH = 30;
        // live mode
        IntPtr dib, memDc, oldBmp, bits;
        Bitmap buffer;

        public static bool Active;

        bool measure; // ruler mode: distances to the next edge, drag measures an area
        readonly bool measureOnly; // opened as ruler, M switches it on and off while snipping
        Bitmap measureShot; // live mode has no frozen shot, the ruler takes one when it starts
        int pw, ph;
        int[] pixels; // measure mode: the frozen shot as plain ints, GetPixel is too slow for edge search

        public Overlay(bool windowMode, bool pickColor = false, bool measureMode = false)
        {
            var s = Settings.Current;
            measure = measureOnly = measureMode;
            frozen = s.Freeze || measure;
            picker = pickOnly = pickColor;
            snap = !picker && !measure && (windowMode || s.Snap);
            virt = SystemInformation.VirtualScreen;
            windows = Native.VisibleWindows();
            if (frozen)
            {
                shot = Imaging.Grab(virt);
                if (measure) LoadPixels(shot);
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
            // No WinForms double buffer: for a window this big it allocates a new buffer on every paint.
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
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
            // One lasting back buffer, frames only redraw and copy what changed.
            var info = new Native.BITMAPINFO { Size = 40, Width = virt.Width, Height = -virt.Height, Planes = 1, BitCount = 32 };
            memDc = Native.CreateCompatibleDC(IntPtr.Zero);
            dib = Native.CreateDIBSection(memDc, ref info, 0, out bits, IntPtr.Zero, 0);
            oldBmp = Native.SelectObject(memDc, dib);
            buffer = new Bitmap(virt.Width, virt.Height, virt.Width * 4, PixelFormat.Format32bppPArgb, bits);
            if (!frozen)
            {
                Native.SetWindowDisplayAffinity(Handle, 0x11); // WDA_EXCLUDEFROMCAPTURE
                Present(new Rectangle(0, 0, virt.Width, virt.Height), true);
            }
            else Invalidate();
            if (picker) { Cursor.Hide(); cursorHidden = true; } // the loupe is the cursor
            Activate();
            Native.ForceForeground(Handle);
            Focus();
        }

        void Present(Rectangle clip, bool full = false)
        {
            clip.Intersect(new Rectangle(0, 0, virt.Width, virt.Height));
            if (clip.Width <= 0 || clip.Height <= 0) return;
            using (var g = Graphics.FromImage(buffer))
            {
                g.SetClip(clip);
                DrawScene(g, clip);
            }
            if (!full && Native.UpdateLayeredDirty(Handle, memDc, clip)) return;
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

        const int PickD = 150, PickCell = 10;

        Rectangle LoupeRect()
        {
            if (picker) return new Rectangle(cur.X - PickD / 2 - 4, cur.Y - PickD / 2 - 4, PickD + 8, PickD + 48);
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
            if (measure) return new List<Tuple<string, string, bool>> {
                Tuple.Create("Esc", L.T("Schließen"), false), Tuple.Create(L.T("Klick"), L.T("Kopieren"), false), Tuple.Create(L.T("Ziehen"), L.T("Fläche messen"), false),
                Tuple.Create("M", L.T("Zurück"), true) }.Take(measureOnly ? 3 : 4).ToList();
            if (picker)
            {
                var pick = new List<Tuple<string, string, bool>> {
                    Tuple.Create("Esc", L.T("Abbrechen"), false), Tuple.Create(L.T("Klick"), L.T("Aufnehmen"), false), Tuple.Create(L.T("Pfeile"), L.T("Feinjustieren"), false) };
                if (!pickOnly) pick.Add(Tuple.Create("Tab", L.T("Zurück"), true));
                return pick;
            }
            var items = new List<Tuple<string, string, bool>> {
                Tuple.Create("Esc", L.T("Abbrechen"), false),
                Tuple.Create("W", "Snap", snap),
                Tuple.Create("C", L.T("Fadenkreuz"), Settings.Current.Crosshair),
                Tuple.Create("Tab", L.T("Farbwähler"), false),
                Tuple.Create("M", L.T("Lineal"), false) };
            if (dragging) items.Insert(1, Tuple.Create(L.T("Leertaste"), L.T("Verschieben"), moving));
            return items;
        }

        string hintKey;
        int hintWidth;

        int HintWidth(List<Tuple<string, string, bool>> items)
        {
            string key = string.Join("|", items.Select(i => i.Item1 + i.Item2));
            if (key == hintKey) return hintWidth; // measuring text every frame adds up
            hintKey = key;
            using (var g = Graphics.FromImage(Measure))
            {
                float w = HintPad * 2 - ItemGap;
                foreach (var i in items) w += g.MeasureString(i.Item1, KeyFont).Width + 10 + CapGap + g.MeasureString(i.Item2, HintFont).Width + ItemGap;
                return hintWidth = (int)Math.Ceiling(w);
            }
        }

        Rectangle HintRect()
        {
            var b = Screen.FromPoint(new Point(cur.X + virt.X, cur.Y + virt.Y)).Bounds;
            int w = HintWidth(HintItems());
            return new Rectangle(b.X - virt.X + (b.Width - w) / 2, b.Y - virt.Y + 18, w, HintH);
        }

        // Label above or inside the selection, see DrawSelection.
        static Rectangle LabelArea(Rectangle r) { return new Rectangle(r.X - 2, r.Y - 34, 200, 68); }

        // A growing selection only changes along its edges, the inside is the same picture.
        static void EdgeStrips(List<Rectangle> parts, Rectangle a, Rectangle b)
        {
            const int m = 6;
            var u = Rectangle.Union(a, b);
            parts.Add(Rectangle.FromLTRB(Math.Min(a.Left, b.Left) - m, u.Top - m, Math.Max(a.Left, b.Left) + m, u.Bottom + m));
            parts.Add(Rectangle.FromLTRB(Math.Min(a.Right, b.Right) - m, u.Top - m, Math.Max(a.Right, b.Right) + m, u.Bottom + m));
            parts.Add(Rectangle.FromLTRB(u.Left - m, Math.Min(a.Top, b.Top) - m, u.Right + m, Math.Max(a.Top, b.Top) + m));
            parts.Add(Rectangle.FromLTRB(u.Left - m, Math.Min(a.Bottom, b.Bottom) - m, u.Right + m, Math.Max(a.Bottom, b.Bottom) + m));
        }

        void Update2()
        {
            var parts = new List<Rectangle>();
            var loupe = Rectangle.Inflate(LoupeRect(), 2, 2);
            var hint = Rectangle.Inflate(HintRect(), 40, 0); // width changes with the items
            var r = Highlight();
            if (redrawAll) { parts.Add(new Rectangle(0, 0, Width, Height)); redrawAll = false; }
            else
            {
                parts.Add(lastLoupe); parts.Add(loupe);
                if (measure) { parts.AddRange(lastMeasure); parts.AddRange(MeasureParts()); }
                if (hint != lastHint) { parts.Add(lastHint); parts.Add(hint); }
                if (r != lastHighlight)
                {
                    if (dragging && !r.IsEmpty && !lastHighlight.IsEmpty) EdgeStrips(parts, lastHighlight, r);
                    else
                    {
                        // a different snapped window: its whole area turns from dim to clear
                        if (!lastHighlight.IsEmpty) parts.Add(Rectangle.Inflate(lastHighlight, 6, 6));
                        if (!r.IsEmpty) parts.Add(Rectangle.Inflate(r, 6, 6));
                    }
                    if (!lastHighlight.IsEmpty) parts.Add(LabelArea(lastHighlight));
                    if (!r.IsEmpty) parts.Add(LabelArea(r));
                }
                if (Settings.Current.Crosshair && cur != lastCur)
                {
                    parts.Add(new Rectangle(0, lastCur.Y - 1, Width, 3)); parts.Add(new Rectangle(lastCur.X - 1, 0, 3, Height));
                    parts.Add(new Rectangle(0, cur.Y - 1, Width, 3)); parts.Add(new Rectangle(cur.X - 1, 0, 3, Height));
                }
            }
            lastLoupe = loupe; lastHint = hint; lastHighlight = r; lastCur = cur;
            if (measure) lastMeasure = MeasureParts();
            foreach (var part in Merge(parts)) Render(part);
        }

        // Overlapping parts become one, far apart parts stay separate.
        static List<Rectangle> Merge(List<Rectangle> parts)
        {
            var list = parts.Where(p => p.Width > 0 && p.Height > 0).ToList();
            for (bool merged = true; merged;)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count; j++)
                        if (list[i].IntersectsWith(list[j])) { list[i] = Rectangle.Union(list[i], list[j]); list.RemoveAt(j); merged = true; break; }
            }
            return list;
        }

        // Draws straight away instead of waiting for WM_PAINT, so frames don't pile up.
        void Render(Rectangle clip)
        {
            clip.Intersect(new Rectangle(0, 0, virt.Width, virt.Height));
            if (clip.Width <= 0 || clip.Height <= 0) return;
            if (buffer == null || !IsHandleCreated) { Invalidate(clip); return; }
            if (!frozen) { Present(clip); return; }
            using (var g = Graphics.FromImage(buffer))
            {
                g.SetClip(clip);
                DrawScene(g, clip);
            }
            IntPtr hdc = Native.GetDC(Handle);
            Native.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, memDc, clip.X, clip.Y, 0x00CC0020);
            Native.ReleaseDC(Handle, hdc);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!frozen) return;
            var clip = e.ClipRectangle;
            clip.Intersect(new Rectangle(0, 0, virt.Width, virt.Height));
            if (buffer == null || clip.Width <= 0 || clip.Height <= 0) { DrawScene(e.Graphics, e.ClipRectangle); return; }
            using (var g = Graphics.FromImage(buffer))
            {
                g.SetClip(clip);
                DrawScene(g, clip);
            }
            IntPtr hdc = e.Graphics.GetHdc();
            Native.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, memDc, clip.X, clip.Y, 0x00CC0020);
            e.Graphics.ReleaseHdc(hdc);
        }

        void DrawScene(Graphics g, Rectangle clip)
        {
            var r = Highlight();
            g.CompositingMode = CompositingMode.SourceCopy;
            if (frozen) g.DrawImage(picker || measure ? shot : dim, clip, clip, GraphicsUnit.Pixel);
            else using (var b = new SolidBrush(Color.FromArgb(picker || measure ? 1 : 120, 0, 0, 0))) g.FillRectangle(b, clip);
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
            if (clip.IntersectsWith(Rectangle.Inflate(LoupeRect(), 4, 4))) DrawLoupe(g); // the live loupe grabs the screen, skip it when it can't show
            if (clip.IntersectsWith(Rectangle.Inflate(HintRect(), 4, 4))) DrawHint(g);
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

        Color Sample(bool darkest)
        {
            var p = new Point(cur.X + virt.X, cur.Y + virt.Y);
            using (var b = frozen ? Imaging.Crop(shot, new Rectangle(cur.X - 10, cur.Y - 10, 21, 21)) : Imaging.Grab(new Rectangle(p.X - 10, p.Y - 10, 21, 21), false))
                return darkest ? Imaging.Darkest(b, new Point(b.Width / 2, b.Height / 2)) : b.GetPixel(b.Width / 2, b.Height / 2);
        }

        // Swaps the square loupe for the round picker without leaving the overlay.
        void EnterPicker()
        {
            picker = true; snap = false;
            if (!cursorHidden) { Cursor.Hide(); cursorHidden = true; }
            redrawAll = true;
            Update2();
        }

        void LoadPixels(Bitmap src)
        {
            pw = src.Width; ph = src.Height;
            var d = src.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            pixels = new int[pw * ph];
            Marshal.Copy(d.Scan0, pixels, 0, pixels.Length);
            src.UnlockBits(d);
        }

        void EnterMeasure()
        {
            if (pixels == null) LoadPixels(shot ?? (measureShot = Imaging.Grab(virt, false)));
            measure = true; snap = false;
            redrawAll = true;
            Update2();
        }

        void LeaveMeasure()
        {
            measure = false; snap = Settings.Current.Snap;
            redrawAll = true;
            Update2();
        }

        void LeavePicker()
        {
            picker = false; snap = Settings.Current.Snap;
            if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
            redrawAll = true;
            Update2();
        }

        void Pick(Color c)
        {
            Clipboard.SetText(Colors.Format(c, Settings.Current.ColorFormat));
            Close();
            if (Settings.Current.ColorAfter == "copy") Sounds.Play(Sounds.Kind.Pick);
            else ColorPanel.Open(c); // plays the sound as it appears
        }

        // Round magnifier that replaces the cursor while picking.
        void DrawPickLoupe(Graphics g)
        {
            int cells = PickD / PickCell;
            var circle = new Rectangle(cur.X - PickD / 2, cur.Y - PickD / 2, PickD, PickD);
            var srcR = new Rectangle(cur.X - cells / 2, cur.Y - cells / 2, cells, cells);
            Color c = Color.Black;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(shadow, Rectangle.Inflate(circle, 4, 4));
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(circle);
                var st = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                var dest = new Rectangle(circle.X, circle.Y, cells * PickCell, cells * PickCell);
                if (frozen)
                {
                    g.DrawImage(shot, dest, srcR, GraphicsUnit.Pixel);
                    if (new Rectangle(0, 0, shot.Width, shot.Height).Contains(cur)) c = shot.GetPixel(cur.X, cur.Y);
                }
                else using (var live = Imaging.Grab(new Rectangle(srcR.X + virt.X, srcR.Y + virt.Y, cells, cells), false))
                {
                    g.DrawImage(live, dest, new Rectangle(0, 0, cells, cells), GraphicsUnit.Pixel);
                    c = live.GetPixel(cells / 2, cells / 2);
                }
                g.PixelOffsetMode = PixelOffsetMode.Default;
                using (var grid = new Pen(Color.FromArgb(22, 128, 128, 128)))
                    for (int i = 1; i < cells; i++)
                    {
                        g.DrawLine(grid, dest.X + i * PickCell, dest.Y, dest.X + i * PickCell, dest.Bottom);
                        g.DrawLine(grid, dest.X, dest.Y + i * PickCell, dest.Right, dest.Y + i * PickCell);
                    }
                var center = new Rectangle(dest.X + cells / 2 * PickCell, dest.Y + cells / 2 * PickCell, PickCell, PickCell);
                g.DrawRectangle(Pens.Black, Rectangle.Inflate(center, 1, 1));
                g.DrawRectangle(Pens.White, center);
                g.Restore(st);
            }
            // The ring shows the color under the center pixel.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(c, 7)) g.DrawEllipse(p, Rectangle.Inflate(circle, -3, -3));
            using (var p = new Pen(Color.FromArgb(90, 0, 0, 0))) g.DrawEllipse(p, circle);
            using (var p = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawEllipse(p, Rectangle.Inflate(circle, -7, -7));
            string hex = Colors.Hex(c);
            using (var f = new Font("Segoe UI Semibold", 9f))
            {
                var size = g.MeasureString(hex, f);
                var pill = new RectangleF(cur.X - (size.Width + 32) / 2, circle.Bottom + 10, size.Width + 32, 26);
                using (var path = Renderer.Round(pill, 26)) using (var b = new SolidBrush(Color.FromArgb(235, 28, 28, 30))) g.FillPath(b, path);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, pill.X + 8, pill.Y + 7, 12, 12);
                using (var p = new Pen(Color.FromArgb(80, 255, 255, 255))) g.DrawEllipse(p, pill.X + 8, pill.Y + 7, 12, 12);
                g.DrawString(hex, f, Brushes.White, pill.X + 24, pill.Y + 5);
            }
        }

        void DrawLoupe(Graphics g)
        {
            if (measure) { if (!dragging) DrawMeasure(g); return; }
            if (picker) { DrawPickLoupe(g); return; }
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
            // Screen coordinates, handy for bug reports and CSS.
            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(Color.FromArgb(150, 150, 156)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far })
                g.DrawString((cur.X + virt.X) + ", " + (cur.Y + virt.Y), f, b, new RectangleF(r.X, dest.Bottom + 8, r.Width - 8, 16), sf);
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
            if (picker)
            {
                if (e.Button == MouseButtons.Left) Pick(Sample((ModifierKeys & Keys.Shift) != 0)); else Close();
                return;
            }
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

        // Hard stops for the ruler: the window under the cursor, or the desktop without the taskbar, never past the monitor.
        Rectangle MeasureBounds(Point p)
        {
            var sp = new Point(p.X + virt.X, p.Y + virt.Y);
            var screen = Screen.FromPoint(sp);
            var info = HoverInfo();
            Rectangle r = info.HasValue && !info.Value.Desktop ? Rectangle.Intersect(info.Value.R, screen.Bounds) : screen.WorkingArea;
            if (!r.Contains(sp)) r = screen.Bounds;
            r.Offset(-virt.X, -virt.Y);
            r.Intersect(new Rectangle(0, 0, pw, ph));
            return r;
        }

        // Edge search: walks from the point until the color changes noticeably.
        int[] Span(Point p, bool horizontal)
        {
            int w = pw, h = ph;
            if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h) return new[] { 0, 0 };
            var bounds = MeasureBounds(p);
            int start = pixels[p.Y * w + p.X], lo = horizontal ? p.X : p.Y, hi = lo;
            int min = horizontal ? bounds.Left : bounds.Top, max = (horizontal ? bounds.Right : bounds.Bottom) - 1;
            Func<int, int> at = i => horizontal ? pixels[p.Y * w + i] : pixels[i * w + p.X];
            Func<int, bool> same = v => Math.Abs(((v >> 16) & 255) - ((start >> 16) & 255)) + Math.Abs(((v >> 8) & 255) - ((start >> 8) & 255)) + Math.Abs((v & 255) - (start & 255)) < 30;
            while (lo > min && same(at(lo - 1))) lo--;
            while (hi < max && same(at(hi + 1))) hi++;
            return new[] { lo, hi + 1 };
        }

        List<Rectangle> lastMeasure = new List<Rectangle>();

        List<Rectangle> MeasureParts()
        {
            var list = new List<Rectangle>();
            if (dragging || pixels == null) return list;
            var hs = Span(cur, true); var vs = Span(cur, false);
            list.Add(Rectangle.FromLTRB(hs[0] - 8, cur.Y - 8, hs[1] + 8, cur.Y + 36));
            list.Add(Rectangle.FromLTRB(cur.X - 8, vs[0] - 8, cur.X + 80, vs[1] + 8));
            return list;
        }

        void DrawMeasure(Graphics g)
        {
            if (pixels == null) return;
            var hs = Span(cur, true); var vs = Span(cur, false);
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Accent))
            {
                g.DrawLine(p, hs[0], cur.Y, hs[1] - 1, cur.Y);
                g.DrawLine(p, hs[0], cur.Y - 5, hs[0], cur.Y + 5); g.DrawLine(p, hs[1] - 1, cur.Y - 5, hs[1] - 1, cur.Y + 5);
                g.DrawLine(p, cur.X, vs[0], cur.X, vs[1] - 1);
                g.DrawLine(p, cur.X - 5, vs[0], cur.X + 5, vs[0]); g.DrawLine(p, cur.X - 5, vs[1] - 1, cur.X + 5, vs[1] - 1);
            }
            MeasureChip(g, (hs[1] - hs[0]) + " px", (hs[0] + hs[1]) / 2f, cur.Y + 8, true);
            MeasureChip(g, (vs[1] - vs[0]) + " px", cur.X + 8, (vs[0] + vs[1]) / 2f - 10, false);
        }

        static void MeasureChip(Graphics g, string s, float x, float y, bool centered)
        {
            using (var f = new Font("Segoe UI", 8.5f))
            {
                var size = g.MeasureString(s, f);
                float left = centered ? x - (size.Width + 8) / 2 : x;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Renderer.Round(new RectangleF(left, y, size.Width + 8, 20), 10))
                using (var b = new SolidBrush(Color.FromArgb(230, 28, 28, 28))) g.FillPath(b, path);
                g.SmoothingMode = SmoothingMode.None;
                g.DrawString(s, f, Brushes.White, left + 4, y + 3);
            }
        }

        // Measure mode stays open, a click or a dragged area copies the size.
        void CopyMeasure(Rectangle area)
        {
            string text;
            if (area.Width >= 4 && area.Height >= 4) text = area.Width + " × " + area.Height;
            else { var hs = Span(cur, true); var vs = Span(cur, false); text = (hs[1] - hs[0]) + " × " + (vs[1] - vs[0]); }
            Clipboard.SetText(text);
            Sounds.Play(Sounds.Kind.Copy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            if (measure)
            {
                cur = e.Location; var area = Selection(); dragging = false;
                CopyMeasure(area);
                redrawAll = true; Update2();
                return;
            }
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
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (e.KeyCode == Keys.M && !pickOnly && !measureOnly && !picker && !dragging) { if (measure) LeaveMeasure(); else EnterMeasure(); return; }
            if (picker || measure) return; // only the keys the bar shows
            if (e.KeyCode == Keys.W) { snap = !snap; Update2(); }
            else if (e.KeyCode == Keys.C)
            {
                Settings.Current.Crosshair = !Settings.Current.Crosshair;
                Settings.Current.Save();
                redrawAll = true;
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
            if (code == Keys.Tab && !pickOnly && !measure && !dragging) { if (picker) LeavePicker(); else EnterPicker(); return true; }
            if (picker && (code == Keys.Tab || code == Keys.Return || code == Keys.Space))
            {
                Pick(Sample((k & Keys.Shift) != 0));
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

        // Test hook: moves the mouse and waits for the frame like a real move would.
        public double TestMoves(int count)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, 200 + (i % 2) * 900 + i * 7, 300 + (i % 3) * 250, 0));
                Update();
            }
            return sw.Elapsed.TotalMilliseconds / count;
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
                if (measureShot != null) { measureShot.Dispose(); measureShot = null; }
                if (cursorHidden) { Cursor.Show(); cursorHidden = false; }
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
