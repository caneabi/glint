using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    public static class Renderer
    {
        public static Color WithOpacity(Obj o) { return Color.FromArgb(o.Opacity * 255 / 100, o.Color); }

        // clip: only that area is redrawn, the rest of 'into' stays as it is.
        public static Bitmap Compose(Bitmap src, List<Obj> objs, Bitmap into = null, Rectangle? clip = null)
        {
            var c = into ?? new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(c))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                if (clip.HasValue) { g.SetClip(clip.Value); g.DrawImage(src, clip.Value, clip.Value, GraphicsUnit.Pixel); }
                else g.DrawImageUnscaled(src, 0, 0);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var spots = objs.Where(o => o.Kind == Kind.Spotlight).ToList();
                if (spots.Count > 0)
                    using (var rgn = new Region(new Rectangle(0, 0, c.Width, c.Height)))
                    {
                        foreach (var s in spots) rgn.Exclude(s.Box);
                        using (var b = new SolidBrush(Color.FromArgb(spots[0].Opacity * 2, 0, 0, 0))) g.FillRegion(b, rgn);
                    }
                foreach (var o in objs)
                    if (o.Kind != Kind.Spotlight && (!clip.HasValue || Reach(o).IntersectsWith(clip.Value))) Draw(g, c, src, o);
            }
            return c;
        }

        // Everything an object can paint on, with room for strokes, arrow heads and shadows.
        public static Rectangle Reach(Obj o)
        {
            RectangleF b = o.Box;
            if (o.Pts != null && o.Pts.Count > 0)
            {
                float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, bo = float.MinValue;
                foreach (var p in o.Pts) { l = Math.Min(l, p.X); t = Math.Min(t, p.Y); r = Math.Max(r, p.X); bo = Math.Max(bo, p.Y); }
                b = RectangleF.FromLTRB(l, t, r, bo);
            }
            if (o.Ctrl.HasValue) b = RectangleF.Union(b, new RectangleF(o.Ctrl.Value, SizeF.Empty));
            if (o.Kind == Kind.Step) { float rr = 12 + o.Width * 1.6f; b = new RectangleF(o.A.X - rr, o.A.Y - rr, rr * 2, rr * 2); }
            int m = (int)(o.Width * 4) + 24;
            return Rectangle.Inflate(Rectangle.Round(b), m, m);
        }

        static Rectangle Clip(RectangleF f, Bitmap b)
        {
            var r = Rectangle.Round(f); r.Intersect(new Rectangle(0, 0, b.Width, b.Height)); return r;
        }

        public static void Draw(Graphics g, Bitmap canvas, Bitmap src, Obj o)
        {
            switch (o.Kind)
            {
                case Kind.Pixelate: Effect(g, src, Clip(o.Box, src), Math.Max(4, (int)o.Width * 3), InterpolationMode.NearestNeighbor); break;
                case Kind.Blur: Effect(g, src, Clip(o.Box, src), Math.Max(4, (int)o.Width * 3), InterpolationMode.HighQualityBicubic); break;
                case Kind.Erase:
                    {
                        var r = Clip(o.Box, src);
                        if (r.Width > 0 && r.Height > 0)
                        {
                            var around = Rectangle.Inflate(r, 2, 2);
                            around.Intersect(new Rectangle(0, 0, src.Width, src.Height));
                            using (var b = new SolidBrush(Border(src, around))) g.FillRectangle(b, r);
                        }
                        break;
                    }
                case Kind.Magnifier: DrawMagnifier(g, canvas, o); break;
                case Kind.Image:
                    {
                        var m = new ColorMatrix { Matrix33 = o.Opacity / 100f };
                        using (var ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(m);
                            var d = Rectangle.Round(o.Box);
                            g.DrawImage(o.Img, d, 0, 0, o.Img.Width, o.Img.Height, GraphicsUnit.Pixel, ia);
                        }
                        break;
                    }
                case Kind.Rect:
                case Kind.Oval: DrawShape(g, o); break;
                case Kind.Arrow:
                case Kind.Line: DrawLine(g, o); break;
                case Kind.Measure: DrawMeasure(g, o); break;
                case Kind.Pen:
                case Kind.Highlight:
                    if (o.Pts != null && o.Pts.Count > 1)
                    {
                        bool hi = o.Kind == Kind.Highlight;
                        var col = hi ? Color.FromArgb(110, o.Color) : WithOpacity(o);
                        using (var p = new Pen(col, hi ? o.Width * 4 : o.Width) { LineJoin = LineJoin.Round, StartCap = hi ? LineCap.Square : LineCap.Round, EndCap = hi ? LineCap.Square : LineCap.Round })
                            g.DrawLines(p, o.Pts.ToArray());
                    }
                    break;
                case Kind.Step:
                    {
                        float r = 12 + o.Width * 1.6f;
                        using (var b = new SolidBrush(WithOpacity(o))) g.FillEllipse(b, o.A.X - r, o.A.Y - r, r * 2, r * 2);
                        using (var f = new Font("Segoe UI", r * .95f, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        using (var b = new SolidBrush(Contrast(o.Color)))
                            g.DrawString(o.Num.ToString(), f, b, o.A, sf);
                        o.B = new PointF(o.A.X + r, o.A.Y + r);
                        break;
                    }
                case Kind.Text:
                    {
                        using (var f = TextFont(o))
                        {
                            var old = g.TextRenderingHint;
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            var s = g.MeasureString(o.Text.Length == 0 ? " " : o.Text, f);
                            o.B = new PointF(o.A.X + s.Width, o.A.Y + s.Height);
                            if (o.Fill)
                                using (var bg = new SolidBrush(o.Color)) g.FillRectangle(bg, o.Box);
                            using (var b = new SolidBrush(o.Fill ? Contrast(o.Color) : WithOpacity(o))) g.DrawString(o.Text, f, b, o.A);
                            g.TextRenderingHint = old;
                        }
                        break;
                    }
            }
        }

        public static float TextSize(Obj o) { return o.Size > 0 ? o.Size : 14 + o.Width * 3; }

        public static Font TextFont(Obj o)
        {
            return new Font(o.Hand ? "Segoe Print" : o.Font, TextSize(o), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        public static Color Contrast(Color c) { return c.R * .299 + c.G * .587 + c.B * .114 > 150 ? Color.Black : Color.White; }

        static Color Border(Bitmap b, Rectangle r)
        {
            long R = 0, G = 0, B = 0, n = 0;
            Action<int, int> add = (x, y) => { var c = b.GetPixel(x, y); R += c.R; G += c.G; B += c.B; n++; };
            for (int x = r.Left; x < r.Right; x += Math.Max(1, r.Width / 60)) { add(x, r.Top); add(x, r.Bottom - 1); }
            for (int y = r.Top; y < r.Bottom; y += Math.Max(1, r.Height / 60)) { add(r.Left, y); add(r.Right - 1, y); }
            return n == 0 ? Color.Black : Color.FromArgb((int)(R / n), (int)(G / n), (int)(B / n));
        }

        static void Effect(Graphics g, Bitmap src, Rectangle r, int block, InterpolationMode mode)
        {
            if (r.Width < 2 || r.Height < 2) return;
            int w = Math.Max(1, r.Width / block), h = Math.Max(1, r.Height / block);
            using (var small = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    sg.DrawImage(src, new Rectangle(0, 0, w, h), r, GraphicsUnit.Pixel);
                }
                var im = g.InterpolationMode; var pm = g.PixelOffsetMode;
                g.InterpolationMode = mode; g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY); // avoids dark edges when upscaling
                    g.DrawImage(small, r, 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
                g.InterpolationMode = im; g.PixelOffsetMode = pm;
            }
        }

        static void DrawMagnifier(Graphics g, Bitmap canvas, Obj o)
        {
            var r = Rectangle.Round(o.Box);
            if (r.Width < 4 || r.Height < 4) return;
            float z = Math.Max(1.25f, o.Zoom);
            int sw = Math.Max(1, (int)(r.Width / z)), sh = Math.Max(1, (int)(r.Height / z));
            var srcR = new Rectangle(r.X + (r.Width - sw) / 2, r.Y + (r.Height - sh) / 2, sw, sh);
            srcR.Intersect(new Rectangle(0, 0, canvas.Width, canvas.Height));
            if (srcR.Width < 1 || srcR.Height < 1) return;
            using (var copy = canvas.Clone(srcR, PixelFormat.Format32bppArgb))
            using (var path = new GraphicsPath())
            {
                if (o.Shape == 1) path.AddPath(Round(r, Math.Min(r.Width, r.Height) * .3f), false);
                else if (o.Shape == 2) path.AddRectangle(r);
                else path.AddEllipse(r);
                var st = g.Save();
                g.SetClip(path);
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(copy, r);
                g.Restore(st);
                using (var p = new Pen(WithOpacity(o), Math.Max(2, o.Width / 1.5f))) g.DrawPath(p, path);
            }
        }

        static PointF[] Wobble(PointF a, PointF b, int seed)
        {
            var rnd = new Random(seed);
            float len = (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            int n = Math.Max(3, (int)(len / 40));
            float amp = Math.Min(3.2f, 1 + len / 120);
            var pts = new PointF[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float j = (i == 0 || i == n) ? 0 : 1;
                pts[i] = new PointF(a.X + (b.X - a.X) * t + j * amp * (float)(rnd.NextDouble() * 2 - 1), a.Y + (b.Y - a.Y) * t + j * amp * (float)(rnd.NextDouble() * 2 - 1));
            }
            return pts;
        }

        static void DrawShape(Graphics g, Obj o)
        {
            var r = o.Box;
            if (r.Width < 1 || r.Height < 1) return;
            using (var p = new Pen(WithOpacity(o), o.Width) { LineJoin = LineJoin.Round })
            using (var path = new GraphicsPath())
            {
                if (o.Kind == Kind.Oval)
                {
                    if (o.Hand)
                    {
                        var rnd = new Random(o.Seed); var pts = new List<PointF>();
                        for (int i = 0; i <= 28; i++)
                        {
                            double a = i / 28.0 * Math.PI * 2.08 - .4; double k = 1 + (rnd.NextDouble() - .5) * .035;
                            pts.Add(new PointF(r.X + r.Width / 2 + (float)(Math.Cos(a) * r.Width / 2 * k), r.Y + r.Height / 2 + (float)(Math.Sin(a) * r.Height / 2 * k)));
                        }
                        path.AddCurve(pts.ToArray(), .6f);
                    }
                    else path.AddEllipse(r);
                }
                else if (o.Hand)
                {
                    var c = new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
                    for (int i = 0; i < 4; i++) { path.StartFigure(); path.AddLines(Wobble(c[i], c[(i + 1) % 4], o.Seed + i)); }
                }
                else path.AddRectangle(r);
                if (o.Fill) using (var b = new SolidBrush(Color.FromArgb(Math.Min(o.Opacity * 255 / 100, 90), o.Color))) g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }

        public static IEnumerable<PointF> Curve(Obj o)
        {
            if (!o.Ctrl.HasValue) { yield return o.A; yield return o.B; yield break; }
            var c = o.Ctrl.Value;
            for (int i = 0; i <= 24; i++)
            {
                float t = i / 24f, u = 1 - t;
                // Ctrl is the on-curve handle at t=.5.
                float qx = 2 * c.X - (o.A.X + o.B.X) / 2, qy = 2 * c.Y - (o.A.Y + o.B.Y) / 2;
                yield return new PointF(u * u * o.A.X + 2 * u * t * qx + t * t * o.B.X, u * u * o.A.Y + 2 * u * t * qy + t * t * o.B.Y);
            }
        }

        static void DrawLine(Graphics g, Obj o)
        {
            var pts = Curve(o).ToList();
            if (o.Hand && !o.Ctrl.HasValue) pts = Wobble(o.A, o.B, o.Seed).ToList();
            var color = WithOpacity(o);
            if (o.Kind == Kind.Arrow && pts.Count >= 2)
            {
                var end = pts[pts.Count - 1]; var before = pts[pts.Count - 2];
                double ang = Math.Atan2(end.Y - before.Y, end.X - before.X);
                float head = 10 + o.Width * 2.6f;
                var l = new PointF(end.X - head * (float)Math.Cos(ang - .45), end.Y - head * (float)Math.Sin(ang - .45));
                var r = new PointF(end.X - head * (float)Math.Cos(ang + .45), end.Y - head * (float)Math.Sin(ang + .45));
                var neck = new PointF((l.X + r.X) / 2, (l.Y + r.Y) / 2);
                pts[pts.Count - 1] = neck;
                using (var p = new Pen(color, o.Width) { StartCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts.ToArray());
                using (var b = new SolidBrush(color)) g.FillPolygon(b, new[] { end, l, r });
            }
            else using (var p = new Pen(color, o.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }) g.DrawLines(p, pts.ToArray());
        }

        public static void DrawMeasure(Graphics g, Obj o)
        {
            using (var p = new Pen(o.Color, 1.5f))
            {
                g.DrawLine(p, o.A, o.B);
                bool horiz = Math.Abs(o.A.X - o.B.X) >= Math.Abs(o.A.Y - o.B.Y);
                float dx = horiz ? 0 : 5, dy = horiz ? 5 : 0;
                g.DrawLine(p, o.A.X - dx, o.A.Y - dy, o.A.X + dx, o.A.Y + dy);
                g.DrawLine(p, o.B.X - dx, o.B.Y - dy, o.B.X + dx, o.B.Y + dy);
            }
            int len = (int)Math.Round(Math.Max(Math.Abs(o.A.X - o.B.X), Math.Abs(o.A.Y - o.B.Y)));
            using (var f = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                string s = len + " px";
                var size = g.MeasureString(s, f);
                float x = (o.A.X + o.B.X) / 2 + 6, y = (o.A.Y + o.B.Y) / 2 + 6;
                using (var b = new SolidBrush(o.Color)) g.FillRectangle(b, x, y, size.Width + 4, size.Height + 2);
                using (var b = new SolidBrush(Contrast(o.Color))) g.DrawString(s, f, b, x + 2, y + 1);
            }
        }

        public static readonly Color[][] Gradients = {
            new[]{Color.FromArgb(255,126,95),Color.FromArgb(254,180,123)},
            new[]{Color.FromArgb(102,126,234),Color.FromArgb(118,75,162)},
            new[]{Color.FromArgb(67,206,162),Color.FromArgb(24,90,157)},
            new[]{Color.FromArgb(240,147,251),Color.FromArgb(245,87,108)},
            new[]{Color.FromArgb(44,62,80),Color.FromArgb(76,92,110)},
            new[]{Color.FromArgb(236,236,240),Color.FromArgb(205,208,216)}};

        public static GraphicsPath Round(RectangleF r, float d)
        {
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        public static Color[] Custom(Color c) { return new[] { c, Color.FromArgb((c.R + 255) / 2, (c.G + 255) / 2, (c.B + 255) / 2) }; }

        public static Bitmap Backdrop(Bitmap img, Color[] cols, int pad)
        {
            var o = BackdropBase(img.Width, img.Height, cols, pad);
            BackdropCompose(o, img, pad, o);
            return o;
        }

        // Gradient and shadow only. It doesn't change while editing, so the editor keeps it.
        public static Bitmap BackdropBase(int w, int h, Color[] cols, int pad)
        {
            var o = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(o))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var all = new Rectangle(0, 0, o.Width, o.Height);
                using (var br = new LinearGradientBrush(all, cols[0], cols[1], 45f)) g.FillRectangle(br, all);
                var inner = new RectangleF(pad, pad, w, h);
                for (int i = 14; i > 0; i--)
                {
                    var s = inner; s.Inflate(i, i); s.Offset(0, 5);
                    using (var path = Round(s, 20 + i)) using (var b = new SolidBrush(Color.FromArgb(7, 0, 0, 0))) g.FillPath(b, path);
                }
            }
            return o;
        }

        // Puts the image with rounded corners onto a copy of the base.
        // area (image coordinates): only that part changed since the last call.
        public static void BackdropCompose(Bitmap bg, Bitmap img, int pad, Bitmap into, Rectangle? area = null)
        {
            using (var g = Graphics.FromImage(into))
            {
                const int d = 18, r = d / 2;
                g.CompositingMode = CompositingMode.SourceCopy;
                if (area.HasValue)
                {
                    var a = area.Value;
                    g.DrawImage(img, new Rectangle(a.X + pad, a.Y + pad, a.Width, a.Height), a, GraphicsUnit.Pixel);
                    var inner = Rectangle.Inflate(new Rectangle(0, 0, img.Width, img.Height), -r, -r);
                    if (inner.Contains(a)) return; // no corner touched
                }
                else
                {
                    if (into != bg) g.DrawImageUnscaled(bg, 0, 0);
                    // A straight copy is fast, only the four corners need the rounded shape.
                    g.DrawImageUnscaled(img, pad, pad);
                }
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Round(new RectangleF(pad, pad, img.Width, img.Height), d))
                using (var tb = new TextureBrush(img, WrapMode.Clamp))
                {
                    tb.TranslateTransform(pad, pad);
                    foreach (var corner in new[] { new Rectangle(pad, pad, r, r), new Rectangle(pad + img.Width - r, pad, r, r), new Rectangle(pad, pad + img.Height - r, r, r), new Rectangle(pad + img.Width - r, pad + img.Height - r, r, r) })
                    {
                        if (into != bg) g.DrawImage(bg, corner, corner, GraphicsUnit.Pixel);
                        g.SetClip(corner);
                        g.FillPath(tb, path);
                        g.ResetClip();
                    }
                }
            }
        }
    }
}
