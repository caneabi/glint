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
    static class Icons
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string> {
            {"select","mouse-pointer-2"},{"crop","crop"},{"arrow","move-up-right"},{"line","slash"},{"rect","square"},{"oval","circle"},
            {"text","type"},{"step","list-ordered"},{"pen","spline"},{"highlight","highlighter"},{"spot","sun"},{"mag","zoom-in"},
            {"pixel","grid-2x2"},{"blur","droplet"},{"erase","eraser"},{"ruler","ruler"},{"undo","undo-2"},{"redo","redo-2"},
            {"backdrop","frame"},{"ocr","scan-text"},{"addcap","image-plus"},{"pin","pin"},{"drag","grip-vertical"},{"save","save"},{"upload","cloud-upload"},{"more","ellipsis"},{"copy","copy"}};
        static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>();

        public static string Name(string id) { string n; return Map.TryGetValue(id, out n) ? n : id; }

        public static void FillLucide(Graphics g, string name, float cx, float cy, float size, float stroke, Color color)
        {
            float k = size / 24f;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(cx - size / 2, cy - size / 2);
            g.ScaleTransform(k, k);
            using (var b = new SolidBrush(color)) g.FillPath(b, PathFor(name));
            using (var pen = new Pen(color, stroke / k) { LineJoin = LineJoin.Round }) g.DrawPath(pen, PathFor(name));
            g.Restore(st);
        }

        static GraphicsPath PathFor(string name)
        {
            GraphicsPath p;
            if (Cache.TryGetValue(name, out p)) return p;
            p = new GraphicsPath();
            var t = IconData.Paths[name].Split(' ');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<int, float> n = k => float.Parse(t[k], inv);
            PointF cur = PointF.Empty, start = PointF.Empty;
            for (int i = 0; i < t.Length; )
            {
                switch (t[i])
                {
                    case "M": p.StartFigure(); cur = start = new PointF(n(i + 1), n(i + 2)); i += 3; break;
                    case "L": { var q = new PointF(n(i + 1), n(i + 2)); p.AddLine(cur, q); cur = q; i += 3; break; }
                    case "C": { var q = new PointF(n(i + 5), n(i + 6)); p.AddBezier(cur, new PointF(n(i + 1), n(i + 2)), new PointF(n(i + 3), n(i + 4)), q); cur = q; i += 7; break; }
                    case "Z": p.CloseFigure(); cur = start; i += 1; break;
                    default: i++; break;
                }
            }
            Cache[name] = p;
            return p;
        }

        public static void DrawLucide(Graphics g, string name, float cx, float cy, float size, float stroke, Color color)
        {
            float k = size / 24f;
            var st = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TranslateTransform(cx - size / 2, cy - size / 2);
            g.ScaleTransform(k, k);
            using (var pen = new Pen(color, stroke / k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawPath(pen, PathFor(name));
            g.Restore(st);
        }

        public static void Draw(Graphics g, string id, Rectangle r)
        {
            DrawLucide(g, Map[id], r.X + r.Width / 2f, r.Y + r.Height / 2f, r.Width * .56f, 1.5f, Color.FromArgb(230, 230, 232));
        }
    }
}
