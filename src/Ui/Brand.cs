using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;

namespace Glint
{
    static class Brand
    {
        public static readonly Color Pixel = Color.FromArgb(253, 83, 76), Tile = Color.FromArgb(28, 28, 30), Light = Color.FromArgb(240, 240, 242), Dark = Color.FromArgb(28, 28, 30);

        // Rounded to whole pixels so it stays sharp at 16 px.
        public static void DrawMark(Graphics g, Rectangle r, Color stroke, Color pixel)
        {
            float u = r.Width / 32f;
            int th = Math.Max(2, (int)Math.Round(3.4f * u)), arm = Math.Max(th + 2, (int)Math.Round(10f * u)), s = Math.Max(th + 2, (int)Math.Round(10f * u));
            int L = r.X, T = r.Y, R = r.Right, B = r.Bottom;
            var old = g.SmoothingMode;
            g.SmoothingMode = r.Width >= 40 ? SmoothingMode.AntiAlias : SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float round = r.Width >= 40 ? th : 0;
            using (var b = new SolidBrush(stroke))
            {
                Action<float, float, float, float> bar = (x, y, w, h) =>
                {
                    if (round == 0) { g.FillRectangle(b, x, y, w, h); return; }
                    using (var p = Renderer.Round(new RectangleF(x, y, w, h), round)) g.FillPath(b, p);
                };
                bar(L, T, arm, th); bar(L, T, th, arm);
                bar(R - arm, T, arm, th); bar(R - th, T, th, arm);
                bar(L, B - th, arm, th); bar(L, B - arm, th, arm);
            }
            using (var b = new SolidBrush(pixel))
            {
                var q = new RectangleF(R - s, B - s, s, s);
                if (r.Width >= 40) using (var p = Renderer.Round(q, s * .36f)) g.FillPath(b, p);
                else g.FillRectangle(b, q);
            }
            g.SmoothingMode = old;
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }

        // Units are x-heights.
        const float Stem = .2f, Gap = .19f;
        static readonly float[] Advance = { 1f, Stem, Stem, .88f, .56f };

        public static float WordmarkWidth(float xHeight)
        {
            float w = Gap * (Advance.Length - 1);
            foreach (var a in Advance) w += a;
            return w * xHeight;
        }

        public static void DrawWordmark(Graphics g, float x, float y, float xHeight, Color ink, Color pixel)
        {
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TranslateTransform(x, y);
            g.ScaleTransform(xHeight, xHeight);
            float h = Stem / 2, r = .5f - h, cx = 0;
            using (var pen = new Pen(ink, Stem) { LineJoin = LineJoin.Round, StartCap = LineCap.Flat, EndCap = LineCap.Flat })
            using (var path = new GraphicsPath())
            {
                // g
                path.AddEllipse(h, h, r * 2, r * 2);
                path.StartFigure();
                path.AddLine(1 - h, 0, 1 - h, 1.02f);
                path.AddArc(1 - h - .7f, 1.02f - .35f, .7f, .7f, 0, 130);
                cx += Advance[0] + Gap;
                // l
                path.StartFigure(); path.AddLine(cx + h, -.58f, cx + h, 1); cx += Advance[1] + Gap;
                // i
                float iX = cx;
                path.StartFigure(); path.AddLine(cx + h, 0, cx + h, 1); cx += Advance[2] + Gap;
                // n
                path.StartFigure(); path.AddLine(cx + h, 0, cx + h, 1);
                path.StartFigure();
                path.AddArc(cx + h, h, .88f - Stem, .88f - Stem, 180, 180);
                path.AddLine(cx + .88f - h, h + (.88f - Stem) / 2, cx + .88f - h, 1);
                cx += Advance[3] + Gap;
                // t
                path.StartFigure();
                path.AddLine(cx + .19f, -.32f, cx + .19f, .68f);
                path.AddArc(cx + .19f, .46f, .44f, .44f, 180, -90);
                path.AddLine(cx + .41f, .9f, cx + .56f, .9f);
                path.StartFigure(); path.AddLine(cx, h, cx + .54f, h);
                g.DrawPath(pen, path);
                // the dot
                using (var b = new SolidBrush(pixel)) g.FillRectangle(b, iX - .035f, -.4f, Stem + .07f, Stem + .07f);
            }
            g.Restore(st);
        }

        public static Bitmap AppBitmap(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float radius = size * .46f;
                using (var p = Renderer.Round(new RectangleF(0, 0, size, size), radius)) using (var b = new SolidBrush(Tile)) g.FillPath(b, p);
                if (size >= 32)
                    using (var p = Renderer.Round(new RectangleF(.5f, .5f, size - 1, size - 1), radius)) using (var pen = new Pen(Color.FromArgb(46, 255, 255, 255))) g.DrawPath(pen, p);
                int inset = Math.Max(2, (int)Math.Round(size * .23f));
                DrawMark(g, new Rectangle(inset, inset, size - inset * 2, size - inset * 2), Light, Pixel);
            }
            return bmp;
        }

        public static bool TaskbarIsLight
        {
            get
            {
                var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return v is int && (int)v == 1;
            }
        }

        public static Bitmap TrayBitmap(int size, bool light)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                int inset = size >= 24 ? 2 : 1;
                DrawMark(g, new Rectangle(inset, inset, size - inset * 2, size - inset * 2), light ? Dark : Light, Pixel);
            }
            return bmp;
        }

        static Icon ToIcon(Bitmap b)
        {
            IntPtr h = b.GetHicon();
            var ic = (Icon)Icon.FromHandle(h).Clone();
            Native.DestroyIcon(h);
            return ic;
        }

        public static Icon TrayIcon()
        {
            using (var b = TrayBitmap(System.Windows.Forms.SystemInformation.SmallIconSize.Width, TaskbarIsLight)) return ToIcon(b);
        }

        static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null) using (var b = AppBitmap(32)) appIcon = ToIcon(b);
                return appIcon;
            }
        }

        public static void WriteIco(string path, int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var b = AppBitmap(s))
                using (var ms = new MemoryStream())
                {
                    b.Save(ms, ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            using (var o = new BinaryWriter(File.Create(path)))
            {
                o.Write((short)0); o.Write((short)1); o.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    o.Write(d); o.Write(d); o.Write((byte)0); o.Write((byte)0);
                    o.Write((short)1); o.Write((short)32);
                    o.Write(images[i].Length); o.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) o.Write(img);
            }
        }
    }
}
