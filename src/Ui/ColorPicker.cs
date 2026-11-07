using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;

namespace Glint
{
    class ColorPicker : Form
    {
        const int W = 300, H = 372, Pad = 16, SquareTop = 16, SquareH = 180, HueTop = 208, HueH = 14, Swatches = 236, HexTop = 274, ButtonTop = 320;
        static readonly Color[] Presets = { Brand.Pixel, Color.FromArgb(255, 170, 40), Color.FromArgb(255, 221, 50), Color.FromArgb(70, 200, 110), Color.FromArgb(60, 140, 255), Color.FromArgb(170, 100, 245), Color.White, Color.FromArgb(28, 28, 30) };
        readonly UiInput hex;
        Bitmap square;
        float h, s, v;
        int drag; // 1 square, 2 hue
        bool syncing;
        public Color Value { get; private set; }

        public ColorPicker(Color start)
        {
            Ui.Refresh();
            Text = "Glint"; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            BackColor = Ui.Bg; DoubleBuffered = true; ClientSize = Ui.S(W, H);
            Value = Color.FromArgb(start.R, start.G, start.B);
            ToHsv(Value, out h, out s, out v);
            hex = new UiInput(Colors.Hex(Value), false, W - Pad * 2 - 46) { BackColor = Ui.Bg, Location = new Point(Ui.S(Pad + 46), Ui.S(HexTop)) };
            hex.Changed = () =>
            {
                if (syncing) return;
                Color c;
                if (!TryParse(hex.Value, out c)) return;
                Value = c; ToHsv(c, out h, out s, out v); RebuildSquare(); Invalidate();
            };
            Controls.Add(hex);
            var cancel = new PillButton(L.T("Abbrechen"), 110, 36) { BackColor = Ui.Bg, Location = new Point(Ui.S(Pad), Ui.S(ButtonTop)) };
            cancel.Changed = () => { DialogResult = DialogResult.Cancel; Close(); };
            var ok = new PrimaryButton(L.T("Übernehmen"), Ui.S(150, 36)) { Location = new Point(Ui.S(W - Pad - 150), Ui.S(ButtonTop)) };
            ok.Changed = () => { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(cancel); Controls.Add(ok);
            RebuildSquare();
        }

        public static bool TryParse(string text, out Color c)
        {
            c = Color.Empty;
            int rgb;
            string t = (text ?? "").Trim().TrimStart('#');
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return false;
            c = Color.FromArgb(rgb >> 16, (rgb >> 8) & 255, rgb & 255);
            return true;
        }

        public static void ToHsv(Color c, out float hue, out float sat, out float val)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            val = max; sat = max == 0 ? 0 : d / max;
            if (d == 0) hue = 0;
            else if (max == r) hue = 60 * (((g - b) / d + 6) % 6);
            else if (max == g) hue = 60 * ((b - r) / d + 2);
            else hue = 60 * ((r - g) / d + 4);
        }

        public static Color FromHsv(float hue, float sat, float val)
        {
            float c = val * sat, x = c * (1 - Math.Abs((hue / 60f) % 2 - 1)), m = val - c, r, g, b;
            int i = (int)(hue / 60f) % 6;
            r = i == 0 || i == 5 ? c : i == 1 || i == 4 ? x : 0;
            g = i == 1 || i == 2 ? c : i == 0 || i == 3 ? x : 0;
            b = i == 3 || i == 4 ? c : i == 2 || i == 5 ? x : 0;
            return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
        }

        void RebuildSquare()
        {
            if (square != null) square.Dispose();
            square = new Bitmap(48, 48, PixelFormat.Format32bppArgb);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                    square.SetPixel(x, y, FromHsv(h, x / 47f, 1 - y / 47f));
        }

        void Sync()
        {
            Value = FromHsv(h, s, v);
            syncing = true; hex.Value = Colors.Hex(Value); syncing = false;
            Invalidate();
        }

        RectangleF SquareRect { get { return new RectangleF(Pad, SquareTop, W - Pad * 2, SquareH); } }
        RectangleF HueRect { get { return new RectangleF(Pad, HueTop, W - Pad * 2, HueH); } }
        RectangleF SwatchRect(int i) { float gap = 8, d = (W - Pad * 2 - gap * (Presets.Length - 1)) / Presets.Length; return new RectangleF(Pad + i * (d + gap), Swatches, d, 26); }

        void Update(PointF p)
        {
            if (drag == 1)
            {
                var r = SquareRect;
                s = Math.Max(0, Math.Min(1, (p.X - r.X) / r.Width));
                v = 1 - Math.Max(0, Math.Min(1, (p.Y - r.Y) / r.Height));
                Sync();
            }
            else if (drag == 2)
            {
                var r = HueRect;
                h = Math.Max(0, Math.Min(359.9f, (p.X - r.X) / r.Width * 360));
                RebuildSquare();
                Sync();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var p = new PointF(e.X / Ui.F, e.Y / Ui.F);
            for (int i = 0; i < Presets.Length; i++)
                if (SwatchRect(i).Contains(p))
                {
                    ToHsv(Presets[i], out h, out s, out v);
                    RebuildSquare(); Sync();
                    return;
                }
            drag = SquareRect.Contains(p) ? 1 : HueRect.Contains(p) ? 2 : 0;
            Update(p);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (drag != 0) Update(new PointF(e.X / Ui.F, e.Y / Ui.F)); }
        protected override void OnMouseUp(MouseEventArgs e) { drag = 0; }

        public void RenderTo(Graphics g) { Paint2(g); }

        void Paint2(Graphics g)
        {
            Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            var r = SquareRect;
            using (var path = Renderer.Round(r, 16))
            {
                var st = g.Save();
                g.SetClip(path);
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    var dest = new[] { r.Location, new PointF(r.Right, r.Y), new PointF(r.X, r.Bottom) };
                    g.DrawImage(square, dest, new RectangleF(0, 0, 48, 48), GraphicsUnit.Pixel, ia);
                }
                g.Restore(st);
            }
            Ring(g, r.X + s * r.Width, r.Y + (1 - v) * r.Height);

            var hr = HueRect;
            using (var path = Renderer.Round(hr, HueH))
            using (var br = new LinearGradientBrush(new RectangleF(hr.X - 1, hr.Y, hr.Width + 2, hr.Height), Color.Red, Color.Red, 0f))
            {
                var blend = new ColorBlend(7);
                blend.Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red };
                blend.Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f };
                br.InterpolationColors = blend;
                g.FillPath(br, path);
            }
            Ring(g, hr.X + h / 360f * hr.Width, hr.Y + HueH / 2f);

            for (int i = 0; i < Presets.Length; i++)
            {
                Ui.Pill(g, SwatchRect(i), Presets[i], 10);
                if (Presets[i].GetBrightness() < .2f)
                    using (var path = Renderer.Round(SwatchRect(i), 10)) using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            }
            Ui.Pill(g, new RectangleF(Pad, HexTop, 38, 28), Value, Ui.ControlRadius);
        }

        static void Ring(Graphics g, float x, float y)
        {
            using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 3.5f)) g.DrawEllipse(p, x - 6, y - 6, 12, 12);
            using (var p = new Pen(Color.White, 2f)) g.DrawEllipse(p, x - 6, y - 6, 12, 12);
        }

        protected override void OnPaint(PaintEventArgs e) { Paint2(e.Graphics); }
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Rounded(Handle); }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if (k == Keys.Enter) { DialogResult = DialogResult.OK; Close(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }

        protected override void Dispose(bool d) { if (d && square != null) square.Dispose(); base.Dispose(d); }
    }
}
