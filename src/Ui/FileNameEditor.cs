using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    class FileNameEditor : Panel
    {
        readonly UiInput input;
        readonly List<VarChip> chips = new List<VarChip>();
        readonly bool urlSafe;
        readonly string ext;
        string hint;

        public FileNameEditor(string pattern, bool urlSafe, string ext, Action<string> changed)
        {
            this.urlSafe = urlSafe; this.ext = ext;
            BackColor = Ui.Card; DoubleBuffered = true; Height = Ui.S(112);
            SetStyle(ControlStyles.ResizeRedraw, true);
            input = new UiInput(pattern, false, 200);
            input.Changed = () => { changed(input.Value); Invalidate(); };
            Controls.Add(input);
            var vars = new[] {
                Tuple.Create("{date}", L.T("Datum, z. B. 2026-11-07")), Tuple.Create("{time}", L.T("Uhrzeit, z. B. 14-30-05")), Tuple.Create("{date:dd.MM.yyyy}", L.T("Datum in eigenem Format")),
                Tuple.Create("{random}", L.T("Zufallswert")), Tuple.Create("{random:6}", L.T("Zufallswert mit fester Länge")), Tuple.Create("{counter}", L.T("Fortlaufende Nummer")), Tuple.Create("{counter:3}", L.T("Nummer mit führenden Nullen")),
                Tuple.Create("{width}", L.T("Bildbreite")), Tuple.Create("{height}", L.T("Bildhöhe")), Tuple.Create("{user}", L.T("Windows-Benutzer")) };
            foreach (var v in vars)
            {
                var chip = new VarChip(v.Item1, v.Item2) { BackColor = Ui.Card };
                chip.Hovered = text => { hint = text; Invalidate(); };
                chips.Add(chip);
                Controls.Add(chip);
            }
            Reflow();
        }

        void Reflow()
        {
            if (input == null) return;
            input.SetBounds(0, 0, Width, input.Height);
            int x = 0, y = input.Bottom + Ui.S(6), gap = Ui.S(4);
            foreach (var c in chips)
            {
                if (x > 0 && x + c.Width > Width) { x = 0; y += c.Height + gap; }
                c.Location = new Point(x, y);
                x += c.Width + gap;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Reflow(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            string text = hint ?? L.T("Vorschau: ") + FileNames.Preview(input.Value, urlSafe, ext);
            using (var b = new SolidBrush(Ui.Dim))
            using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Ui.Small, b, new RectangleF(0, Height / Ui.F - 18, Width / Ui.F, 18), sf);
        }
    }
}
