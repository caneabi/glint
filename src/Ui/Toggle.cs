using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class Toggle : UiControl
    {
        public bool On;
        public Toggle(bool on) { On = on; Size = Ui.S(38, 22); }
        protected override void OnMouseDown(MouseEventArgs e) { On = !On; Fire(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Pill(g, new RectangleF(0, 0, LW, LH), On ? Ui.Accent : Ui.Well, LH);
            float m = Ui.S(2) / Ui.F; // whole pixels keep the knob centered at any scale
            float x = On ? LW - LH + m : m;
            g.FillEllipse(Brushes.White, x, m, LH - m * 2, LH - m * 2);
        }
    }
}
