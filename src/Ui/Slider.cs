using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class Slider : UiControl
    {
        readonly int min, max, step;
        public int Value;
        public Slider(int min, int max, int step, int value, int width)
        {
            this.min = min; this.max = max; this.step = step;
            Value = Math.Max(min, Math.Min(max, value));
            Size = Ui.S(width, 26);
        }
        float TrackW { get { return LW - 52; } }
        void SetFrom(int x)
        {
            int v = min + (int)Math.Round((x / Ui.F - 8.0) / (TrackW - 16) * (max - min) / step) * step;
            v = Math.Max(min, Math.Min(max, v));
            if (v != Value) { Value = v; Fire(); }
        }
        protected override void OnMouseDown(MouseEventArgs e) { SetFrom(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetFrom(e.X); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            float cy = LH / 2f, kx = 8 + (TrackW - 16) * (Value - min) / (float)(max - min);
            Ui.Pill(g, new RectangleF(0, cy - 2, TrackW, 4), Ui.Well, 4);
            Ui.Pill(g, new RectangleF(0, cy - 2, kx, 4), Ui.Accent, 4);
            int knob = Ui.S(14);
            Ui.Disc(g, (int)Math.Round(kx * Ui.F) - knob / 2, (Height - knob) / 2, knob, Color.White);
            Ui.Center(g, Value + " %", Ui.Small, Ui.Text, new RectangleF(TrackW + 4, 0, 48, LH));
        }
    }
}
