using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    class SettingsPage : Panel
    {
        public const int CardX = Ui.Pad, CardY = 72, RowH = Ui.RowH, LabelW = 170;
        class Row { public string Label; public Control[] Right; public Control Stretch, Block; public float LabelWidth; public int H = RowH; }
        readonly string title, subtitle;
        readonly List<Row> rows = new List<Row>();

        public SettingsPage(string title, Size size, string subtitle = null)
        {
            this.title = title; this.subtitle = subtitle; Size = size; BackColor = Ui.Bg; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public int RowCount { get { return rows.Count; } }
        public int ContentHeight { get { return Ui.S(CardY + rows.Sum(r => r.H) + 16); } }

        public void Reset()
        {
            foreach (var c in Controls.Cast<Control>().ToList()) c.Dispose();
            rows.Clear();
            Invalidate();
        }

        public void AddRow(string label, params Control[] right) { Add(label, null, right); }

        // A label with a full-width control below it.
        public void AddBlock(string label, Control block, int height)
        {
            rows.Add(new Row { Label = label, Right = new Control[0], Block = block, H = height });
            Controls.Add(block);
            Reflow();
        }

        // The stretch control takes the room left next to the label.
        public void AddStretch(string label, Control stretch, params Control[] right) { Add(label, stretch, right); }

        void Add(string label, Control stretch, Control[] right)
        {
            rows.Add(new Row { Label = label, Right = right, Stretch = stretch });
            foreach (var c in right) Controls.Add(c);
            if (stretch != null) Controls.Add(stretch);
            Reflow();
        }

        void Reflow()
        {
            int cursor = CardY;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                int top = Ui.S(cursor), rowH = Ui.S(RowH), x = Width - Ui.S(CardX + 14), gap = Ui.S(6);
                cursor += row.H;
                if (row.Block != null)
                {
                    row.Block.SetBounds(Ui.S(CardX + 14), top + Ui.S(RowH - 8), Width - Ui.S((CardX + 14) * 2), row.Block.Height);
                    row.LabelWidth = (Width - Ui.S((CardX + 14) * 2)) / Ui.F;
                    continue;
                }
                for (int k = row.Right.Length - 1; k >= 0; k--)
                {
                    var c = row.Right[k];
                    x -= c.Width;
                    c.Location = new Point(x, top + (rowH - c.Height) / 2);
                    x -= gap;
                }
                if (row.Stretch != null)
                {
                    int left = Ui.S(CardX + 14 + LabelW);
                    row.Stretch.SetBounds(left, top + (rowH - row.Stretch.Height) / 2, Math.Max(Ui.S(60), x - left), row.Stretch.Height);
                    x = left;
                }
                row.LabelWidth = (x - Ui.S(CardX + 14) - gap) / Ui.F;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }

        // Edges and the title area belong to the window, for resizing and dragging.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int edge = Ui.S(6);
                if (p.X >= Width - edge || p.Y >= Height - edge || p.Y < Ui.S(CardY)) { m.Result = new IntPtr(-1); return; }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            float w = Width / Ui.F;
            using (var b = new SolidBrush(Ui.Text)) g.DrawString(title, Ui.Title, b, CardX - 2, 4);
            if (subtitle != null) using (var b = new SolidBrush(Ui.Dim)) g.DrawString(subtitle, Ui.Small, b, CardX - 1, 38);
            if (rows.Count > 0) Ui.Pill(g, new RectangleF(CardX, CardY, w - CardX * 2, rows.Sum(r => r.H)), Ui.Card, Ui.CardRadius);
            int y = CardY - rows.Select(r => r.H).FirstOrDefault();
            for (int i = 0; i < rows.Count; i++)
            {
                y += i == 0 ? rows[0].H : rows[i - 1].H;
                if (i > 0) Ui.Hairline(g, CardX + 14, y, (int)w - CardX, y);
                using (var b = new SolidBrush(Ui.Text))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(rows[i].Label, Ui.Font, b, new RectangleF(CardX + 14, y, Math.Max(10, rows[i].LabelWidth), RowH), sf);
            }
            base.OnPaint(e);
        }
    }
}
