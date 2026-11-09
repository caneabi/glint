using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class PillButton : UiControl
    {
        public string Caption;
        public PillButton(string text, int width, int height = 28) { Caption = text; Size = Ui.S(width, height); }
        protected override void OnMouseDown(MouseEventArgs e) { Fire(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Pill(g, new RectangleF(0, 0, LW, LH), hot ? Color.FromArgb(72, 72, 78) : Ui.Well, Ui.ControlRadius);
            Ui.Center(g, Caption, Ui.Small, Ui.Text, new RectangleF(8, 0, LW - 16, LH));
        }
    }
}
