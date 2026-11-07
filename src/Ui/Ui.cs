using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Glint
{
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(28, 28, 30), Side = Color.FromArgb(36, 36, 38), Card = Color.FromArgb(44, 44, 48),
            Line = Color.FromArgb(58, 58, 63), Well = Color.FromArgb(60, 60, 65), Text = Color.FromArgb(235, 235, 238), Dim = Color.FromArgb(150, 150, 156), Accent = Brand.Pixel;
        // Pixel sizes, windows scale them through the graphics transform.
        public static readonly Font Font = new Font("Segoe UI", 12.5f, GraphicsUnit.Pixel), Small = new Font("Segoe UI", 12f, GraphicsUnit.Pixel), Title = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel);
        public static float F = 1f;

        public static float FactorFor(int screenHeight, float dpi)
        {
            float dpiScale = dpi / 96f, logical = screenHeight / dpiScale;
            float size = Math.Max(1f, Math.Min(1.5f, logical / 1080f));
            return (float)Math.Round(dpiScale * size * 20) / 20;
        }

        public static void Refresh()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) F = FactorFor(screen.Bounds.Height, g.DpiY);
        }

        public static int S(float v) { return (int)Math.Round(v * F); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }
        public const int Pad = 28, RowH = 44, CardRadius = 22, ControlRadius = 14;

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half; // otherwise shapes sit half a pixel up and left
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
        public static void Pill(Graphics g, RectangleF r, Color c, float d)
        {
            using (var p = Renderer.Round(r, d)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }
        static readonly System.Collections.Generic.Dictionary<long, Bitmap> Discs = new System.Collections.Generic.Dictionary<long, Bitmap>();

        // GDI+ antialiasing isn't symmetric, these circles are sampled by hand. Position and size in device pixels.
        public static void Disc(Graphics g, int x, int y, int d, Color c)
        {
            if (d <= 0) return;
            long key = ((long)d << 32) | (uint)c.ToArgb();
            Bitmap b;
            if (!Discs.TryGetValue(key, out b))
            {
                const int N = 8;
                b = new Bitmap(d, d, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                float r = d / 2f;
                for (int py = 0; py < d; py++)
                    for (int px = 0; px < d; px++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < N; sy++)
                            for (int sx = 0; sx < N; sx++)
                            {
                                float dx = px + (sx + .5f) / N - r, dy = py + (sy + .5f) / N - r;
                                if (dx * dx + dy * dy <= r * r) hits++;
                            }
                        b.SetPixel(px, py, Color.FromArgb(c.A * hits / (N * N), c));
                    }
                Discs[key] = b;
            }
            var state = g.Save();
            g.ResetTransform();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(b, new Rectangle(x, y, d, d));
            g.Restore(state);
        }

        // Crisp rectangle in device pixels.
        public static void Block(Graphics g, int x, int y, int w, int h, Color c)
        {
            var state = g.Save();
            g.ResetTransform();
            g.SmoothingMode = SmoothingMode.None;
            using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h);
            g.Restore(state);
        }

        // No AA, it would smear over two pixels.
        public static void Hairline(Graphics g, int x1, int y1, int x2, int y2)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(Line, 0)) g.DrawLine(p, x1, y1, x2, y2);
            g.SmoothingMode = old;
        }
        public static void Center(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisPath, FormatFlags = StringFormatFlags.NoWrap })
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, r, sf);
        }
    }
}
