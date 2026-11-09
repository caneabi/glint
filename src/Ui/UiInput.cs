using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class UiInput : Panel
    {
        readonly TextBox box;
        public Action Changed;
        public UiInput(string value, bool secret, int width)
        {
            Size = Ui.S(width, 28); BackColor = Ui.Card; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Ui.Well, ForeColor = Ui.Text, Font = new Font("Segoe UI", 12f * Ui.F, GraphicsUnit.Pixel), Text = value ?? "", UseSystemPasswordChar = secret };
            box.TextChanged += delegate { if (Changed != null) Changed(); };
            Controls.Add(box);
            PlaceBox();
        }
        public string Value { get { return box.Text; } set { box.Text = value; } }
        public void MakeMultiline()
        {
            box.Multiline = true; box.AcceptsReturn = true; box.AcceptsTab = true; box.ScrollBars = ScrollBars.Vertical; box.WordWrap = true;
            box.Font = new Font("Consolas", 12f * Ui.F, GraphicsUnit.Pixel);
            PlaceBox();
        }
        void PlaceBox()
        {
            int m = Ui.S(10);
            if (box.Multiline) { box.SetBounds(m, m, Width - m * 2, Height - m * 2); return; }
            box.SetBounds(m, (Height - box.Height) / 2, Width - m * 2, box.Height);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (box != null) PlaceBox(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Ui.Prepare(e.Graphics);
            e.Graphics.ScaleTransform(Ui.F, Ui.F);
            Ui.Pill(e.Graphics, new RectangleF(0, 0, Width / Ui.F, Height / Ui.F), Ui.Well, Ui.ControlRadius);
        }
    }
}
