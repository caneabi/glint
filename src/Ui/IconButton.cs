using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class IconButton : UiControl
    {
        readonly string icon;
        public IconButton(string icon, int size) { this.icon = icon; Size = Ui.S(size, size); }
        protected override void OnMouseDown(MouseEventArgs e) { Fire(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            if (hot) Ui.Pill(g, new RectangleF(0, 0, LW - 1, LH - 1), Ui.Well, LW / 2f);
            Icons.DrawLucide(g, icon, LW / 2f, LH / 2f, 13, 1.4f, hot ? Ui.Text : Ui.Dim);
        }
    }
}
