using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    // Click copies the variable.
    class VarChip : UiControl
    {
        static readonly Font Mono = new Font("Consolas", 11.5f, GraphicsUnit.Pixel);
        public readonly string Variable, Hint;
        public Action<string> Hovered;
        bool copied;
        readonly Timer reset = new Timer { Interval = 900 };

        public VarChip(string variable, string hint)
        {
            Variable = variable; Hint = hint; BackColor = Ui.Bg;
            int w = TextRenderer.MeasureText(variable, new Font("Consolas", 11.5f * Ui.F, GraphicsUnit.Pixel)).Width;
            Size = new Size(w + Ui.S(10), Ui.S(24));
            reset.Tick += delegate { reset.Stop(); copied = false; Invalidate(); };
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Hovered != null) Hovered(Hint + "  ·  " + L.T("Klicken zum Kopieren")); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (Hovered != null) Hovered(null); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Clipboard.SetText(Variable);
            copied = true; reset.Stop(); reset.Start();
            Invalidate();
            if (Hovered != null) Hovered(L.T("Kopiert") + ": " + Variable);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Pill(g, new RectangleF(0, 0, LW, LH), hot ? Ui.Well : Ui.Card, 12);
            if (copied) Icons.DrawLucide(g, "check", LW / 2f, LH / 2f, 14, 1.8f, Ui.Accent);
            else Ui.Center(g, Variable, Mono, hot ? Ui.Text : Color.FromArgb(205, 205, 210), new RectangleF(0, 0, LW, LH));
        }

        protected override void Dispose(bool d) { if (d) reset.Dispose(); base.Dispose(d); }
    }

    // Color preview, opens the picker.
    class ColorSwatch : UiControl
    {
        public Func<Color> Current;
        public ColorSwatch() { Size = Ui.S(38, 28); }
        protected override void OnMouseDown(MouseEventArgs e) { Fire(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Pill(g, new RectangleF(0, 0, LW, LH), Current != null ? Current() : Ui.Well, Ui.ControlRadius);
            using (var path = Renderer.Round(new RectangleF(.5f, .5f, LW - 1, LH - 1), Ui.ControlRadius)) using (var p = new Pen(hot ? Ui.Text : Ui.Line)) g.DrawPath(p, path);
        }
    }
}
