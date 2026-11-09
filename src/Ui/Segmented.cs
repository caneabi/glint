using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class Segmented : UiControl
    {
        readonly string[] names, values;
        public int Index;
        public string Value { get { return values[Index]; } }
        public Segmented(string[] values, string[] names, string current, int width)
        {
            this.values = values; this.names = names;
            Index = Math.Max(0, Array.IndexOf(values, current));
            Size = Ui.S(width, 28);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = Math.Max(0, Math.Min(names.Length - 1, e.X * names.Length / Width));
            if (i != Index) { Index = i; Fire(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Pill(g, new RectangleF(0, 0, LW, LH), Color.FromArgb(34, 34, 37), Ui.ControlRadius);
            float w = LW / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new RectangleF(i * w, 0, w, LH);
                if (i == Index) Ui.Pill(g, new RectangleF(r.X + 2, 2, w - 4, LH - 4), Ui.Well, Ui.ControlRadius - 2);
                Ui.Center(g, names[i], Ui.Small, i == Index ? Ui.Text : Ui.Dim, r);
            }
        }
    }
}
