using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glint
{
    class WindowBgPicker : UiControl
    {
        static readonly string[] Modes = { "wallpaper", "transparent", "color", "trim" };
        Bitmap wallpaper;
        int hover = -1;

        public WindowBgPicker() { BackColor = Ui.Card; Height = Ui.S(92); }

        string Label(int i)
        {
            switch (Modes[i])
            {
                case "wallpaper": return L.T("Hintergrundbild");
                case "transparent": return "Transparent";
                case "color": return Settings.Current.WindowBgColor.ToUpperInvariant();
                default: return L.T("Ohne Rand");
            }
        }

        RectangleF Tile(int i)
        {
            float gap = 12, w = (LW - gap * 3) / 4;
            return new RectangleF(i * (w + gap), 0, w, 66);
        }

        int At(Point p)
        {
            var l = new PointF(p.X / Ui.F, p.Y / Ui.F);
            for (int i = 0; i < 4; i++)
            {
                var t = Tile(i);
                if (new RectangleF(t.X, 0, t.Width, LH).Contains(l)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = At(e.Location);
            if (i < 0) return;
            var s = Settings.Current;
            if (Modes[i] == "color")
            {
                Color start;
                if (!ColorPicker.TryParse(s.WindowBgColor, out start)) start = Color.FromArgb(64, 68, 72);
                using (var p = new ColorPicker(start))
                    if (p.ShowDialog(FindForm()) == DialogResult.OK) s.WindowBgColor = Colors.Hex(p.Value);
            }
            s.WindowBg = Modes[i];
            s.Save();
            Fire();
        }

        static void MiniWindow(Graphics g, RectangleF t, bool shadow)
        {
            var w = RectangleF.Inflate(t, -t.Width * .17f, -t.Height * .2f);
            w.Offset(0, 2);
            if (shadow)
                for (int i = 4; i > 0; i--)
                {
                    var r = RectangleF.Inflate(w, i, i); r.Offset(0, 1);
                    using (var path = Renderer.Round(r, 10 + i)) using (var b = new SolidBrush(Color.FromArgb(22, 0, 0, 0))) g.FillPath(b, path);
                }
            Ui.Pill(g, w, Color.FromArgb(242, 242, 245), 10);
            using (var p = new Pen(Color.FromArgb(205, 205, 210))) g.DrawLine(p, w.X, w.Y + 10, w.Right, w.Y + 10);
            using (var b = new SolidBrush(Color.FromArgb(150, 150, 156)))
                for (int i = 0; i < 3; i++) g.FillRectangle(b, w.Right - 7 - i * 6, w.Y + 4, 3, 3);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var s = Settings.Current;
            for (int i = 0; i < 4; i++)
            {
                var t = Tile(i);
                bool on = s.WindowBg == Modes[i];
                using (var path = Renderer.Round(t, Ui.ControlRadius))
                {
                    var st = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    switch (Modes[i])
                    {
                        case "wallpaper":
                            if (wallpaper == null) wallpaper = Imaging.WallpaperThumb(240);
                            if (wallpaper != null)
                            {
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(wallpaper, t);
                            }
                            else g.FillRectangle(Brushes.DimGray, t);
                            break;
                        case "transparent":
                            using (var b = new SolidBrush(Color.FromArgb(70, 70, 75))) g.FillRectangle(b, t);
                            using (var b = new SolidBrush(Color.FromArgb(50, 50, 54)))
                                for (float x = t.X; x < t.Right; x += 8)
                                    for (float y = t.Y; y < t.Bottom; y += 8)
                                        if (((int)((x - t.X) / 8) + (int)((y - t.Y) / 8)) % 2 == 0) g.FillRectangle(b, x, y, 8, 8);
                            break;
                        case "color":
                            Color c;
                            if (!ColorPicker.TryParse(s.WindowBgColor, out c)) c = Ui.Well;
                            using (var b = new SolidBrush(c)) g.FillRectangle(b, t);
                            break;
                        default:
                            using (var b = new SolidBrush(Ui.Bg)) g.FillRectangle(b, t);
                            break;
                    }
                    g.Restore(st);
                    if (Modes[i] == "trim") MiniWindow(g, RectangleF.Inflate(t, 8, 8), false);
                    else MiniWindow(g, t, true);
                    using (var p = new Pen(on ? Ui.Accent : hover == i ? Ui.Dim : Ui.Line, on ? 2f : 1f)) g.DrawPath(p, path);
                }
                Ui.Center(g, Label(i), Ui.Small, on ? Ui.Text : Ui.Dim, new RectangleF(t.X, 70, t.Width, 20));
            }
        }

        protected override void Dispose(bool d) { if (d && wallpaper != null) wallpaper.Dispose(); base.Dispose(d); }
    }
}
