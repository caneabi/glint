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
            Ui.Pill(g, new RectangleF(0, 0, LW - 1, LH - 1), On ? Ui.Accent : Ui.Well, LH - 1);
            float x = On ? LW - LH + 2 : 2;
            g.FillEllipse(Brushes.White, x, 2, LH - 5, LH - 5);
        }
    }
}
