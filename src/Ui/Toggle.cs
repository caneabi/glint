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
            int w = Width, h = Height, m = Ui.S(2);
            Color track = On ? Ui.Accent : Ui.Well;
            Ui.Disc(g, 0, 0, h, track);
            Ui.Disc(g, w - h, 0, h, track);
            Ui.Block(g, h / 2, 0, w - h, h, track);
            Ui.Disc(g, On ? w - h + m : m, m, h - m * 2, Color.White);
        }
    }
}
