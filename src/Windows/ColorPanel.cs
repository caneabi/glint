using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    // Picked colors: two slots with their contrast, every format to copy, recent history.
    class ColorPanel : ResizableForm
    {
        const int W = 320, H = 478, Pad = 20, HistoryMax = 12;
        static ColorPanel instance;

        readonly Color[] slots = { Color.White, Color.Black };
        int active, next; // active: shown in the formats, next: where the next pick goes
        int hoverRow = -1, copiedRow = -1, hoverHistory = -1;
        string hoverButton;
        readonly Timer copiedTimer = new Timer { Interval = 900 };

        static readonly Font Mono = new Font("Consolas", 12f, GraphicsUnit.Pixel), Big = new Font("Segoe UI Semibold", 22f, GraphicsUnit.Pixel),
            Head = new Font("Segoe UI Semibold", 15f, GraphicsUnit.Pixel), Badge = new Font("Segoe UI Semibold", 10.5f, GraphicsUnit.Pixel);

        static readonly Rectangle PickR = new Rectangle(W - 80, 14, 28, 28), CloseR = new Rectangle(W - 46, 14, 28, 28),
            SwapR = new Rectangle(W / 2 - 14, 86, 28, 28);
        static Rectangle SlotR(int i) { int w = (W - Pad * 2 - 12) / 2; return new Rectangle(Pad + i * (w + 12), 54, w, 86); }
        const int ContrastY = 152, FormatsY = 230, RowH = 34, HistoryY = 420;
        static Rectangle RowR(int i) { return new Rectangle(Pad, FormatsY + i * RowH, W - Pad * 2, RowH); }
        static Rectangle DotR(int i) { return new Rectangle(Pad + i * 24, HistoryY + 22, 18, 18); }

        public static void Open(Color c)
        {
            if (instance == null || instance.IsDisposed) instance = new ColorPanel();
            instance.Add(c);
            if (!instance.Visible) { instance.PlaceNearCursor(); instance.Show(); }
            instance.Activate();
        }

        ColorPanel()
        {
            Text = L.T("Glint – Farbe");
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Font = Ui.Font;
            ClientSize = Ui.S(W, H);
            MinimumSize = MaximumSize = Size;
            KeyPreview = true;
            var history = Settings.Current.ColorHistory;
            // The last pick becomes the background for the new one.
            if (history.Count > 0) { Color c; if (ColorPicker.TryParse(history[0], out c)) slots[1] = c; }
            if (history.Count > 1) { Color c; if (ColorPicker.TryParse(history[1], out c)) slots[0] = c; }
            copiedTimer.Tick += delegate { copiedTimer.Stop(); copiedRow = -1; Invalidate(); };
        }

        void PlaceNearCursor()
        {
            var p = Cursor.Position;
            var wa = Screen.FromPoint(p).WorkingArea;
            int x = p.X + 24, y = p.Y - Height / 2;
            if (x + Width > wa.Right) x = p.X - Width - 24;
            Location = new Point(Math.Max(wa.X, Math.Min(x, wa.Right - Width)), Math.Max(wa.Y, Math.Min(y, wa.Bottom - Height)));
        }

        // New picks go into the selected slot, the next one into the other, so two picks give a contrast pair.
        void Add(Color c)
        {
            slots[next] = Color.FromArgb(255, c);
            active = next;
            next = 1 - next;
            var list = Settings.Current.ColorHistory;
            string hex = Colors.Hex(c);
            list.Remove(hex);
            list.Insert(0, hex);
            while (list.Count > HistoryMax) list.RemoveAt(list.Count - 1);
            try { Settings.Current.Save(); } catch (Exception ex) { Log.Error("Farbverlauf speichern", ex); }
            Invalidate();
        }

        List<Color> History()
        {
            var r = new List<Color>();
            foreach (var h in Settings.Current.ColorHistory) { Color c; if (ColorPicker.TryParse(h, out c)) r.Add(c); }
            return r;
        }

        static Point Logical(Point p) { return new Point((int)(p.X / Ui.F), (int)(p.Y / Ui.F)); }

        protected override bool IsControlAt(Point p)
        {
            var l = Logical(p);
            if (PickR.Contains(l) || CloseR.Contains(l) || SwapR.Contains(l) || SlotR(0).Contains(l) || SlotR(1).Contains(l)) return true;
            for (int i = 0; i < Colors.Formats.Length; i++) if (RowR(i).Contains(l)) return true;
            for (int i = 0; i < History().Count; i++) if (DotR(i).Contains(l)) return true;
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var l = Logical(e.Location);
            int row = -1, dot = -1;
            for (int i = 0; i < Colors.Formats.Length; i++) if (RowR(i).Contains(l)) row = i;
            for (int i = 0; i < History().Count; i++) if (DotR(i).Contains(l)) dot = i;
            string button = PickR.Contains(l) ? "pick" : CloseR.Contains(l) ? "close" : SwapR.Contains(l) ? "swap" : null;
            if (row != hoverRow || dot != hoverHistory || button != hoverButton) { hoverRow = row; hoverHistory = dot; hoverButton = button; Invalidate(); }
            Cursor = row >= 0 || dot >= 0 || button != null || SlotR(0).Contains(l) || SlotR(1).Contains(l) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { hoverRow = hoverHistory = -1; hoverButton = null; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var l = Logical(e.Location);
            if (CloseR.Contains(l)) { Close(); return; }
            if (PickR.Contains(l)) { PickAgain(); return; }
            if (SwapR.Contains(l)) { var t = slots[0]; slots[0] = slots[1]; slots[1] = t; Invalidate(); return; }
            for (int i = 0; i < 2; i++) if (SlotR(i).Contains(l)) { active = next = i; Invalidate(); return; }
            for (int i = 0; i < Colors.Formats.Length; i++) if (RowR(i).Contains(l)) { CopyRow(i); return; }
            var history = History();
            for (int i = 0; i < history.Count; i++) if (DotR(i).Contains(l)) { slots[active] = history[i]; Invalidate(); return; }
        }

        void CopyRow(int i)
        {
            Clipboard.SetText(Colors.Format(slots[active], Colors.Formats[i]));
            Sounds.Play(Sounds.Kind.Copy);
            copiedRow = i; copiedTimer.Stop(); copiedTimer.Start();
            Invalidate();
        }

        void PickAgain()
        {
            if (Overlay.Active) return;
            Hide();
            var o = new Overlay(false, true);
            Overlay.Active = true;
            o.FormClosed += delegate { if (!IsDisposed && !Visible) { Show(); Activate(); } };
            o.Show();
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            if (k == Keys.Space || k == Keys.P) { PickAgain(); return true; }
            if (k == Keys.X) { var t = slots[0]; slots[0] = slots[1]; slots[1] = t; Invalidate(); return true; }
            if (k == (Keys.Control | Keys.C)) { CopyRow(Math.Max(0, Array.IndexOf(Colors.Formats, Settings.Current.ColorFormat))); return true; }
            return base.ProcessCmdKey(ref m, k);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            using (var b = new SolidBrush(Ui.Text)) g.DrawString(L.T("Farbe"), Head, b, Pad - 2, 17);
            IconButton(g, PickR, "droplet", "pick");
            IconButton(g, CloseR, "x", "close");

            for (int i = 0; i < 2; i++) DrawSlot(g, i);
            using (var b = new SolidBrush(hoverButton == "swap" ? Ui.Well : Ui.Side)) g.FillEllipse(b, SwapR);
            using (var p = new Pen(Ui.Bg, 3)) g.DrawEllipse(p, SwapR);
            Icons.DrawLucide(g, "arrow-left-right", SwapR.X + 14, SwapR.Y + 14, 14, 1.6f, Ui.Text);

            DrawContrast(g);
            DrawFormats(g);
            DrawHistory(g);
        }

        void IconButton(Graphics g, Rectangle r, string icon, string id)
        {
            if (hoverButton == id) Ui.Pill(g, r, id == "close" ? Color.FromArgb(232, 17, 35) : Ui.Well, r.Width);
            Icons.DrawLucide(g, icon, r.X + r.Width / 2f, r.Y + r.Height / 2f, 15, 1.5f, hoverButton == id ? Ui.Text : Ui.Dim);
        }

        void DrawSlot(Graphics g, int i)
        {
            var r = SlotR(i);
            var c = slots[i];
            Ui.Pill(g, r, c, 18);
            if (i == active) using (var path = Renderer.Round(RectangleF.Inflate(r, 2.5f, 2.5f), 22)) using (var p = new Pen(Ui.Accent, 2)) g.DrawPath(p, path);
            else using (var path = Renderer.Round(r, 18)) using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(p, path);
            using (var b = new SolidBrush(Color.FromArgb(200, Renderer.Contrast(c))))
            {
                g.DrawString(i == 0 ? L.T("Vordergrund") : L.T("Hintergrund"), Ui.Small, b, r.X + 12, r.Bottom - 38);
                g.DrawString(Colors.Hex(c), Mono, b, r.X + 12, r.Bottom - 22);
            }
        }

        void DrawContrast(Graphics g)
        {
            var r = new Rectangle(Pad, ContrastY, W - Pad * 2, 66);
            Ui.Pill(g, r, Ui.Card, 18);
            double ratio = Colors.Wcag(slots[0], slots[1]), lc = Colors.Apca(slots[0], slots[1]);
            using (var b = new SolidBrush(Ui.Text)) g.DrawString(ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " : 1", Big, b, r.X + 12, r.Y + 8);
            using (var b = new SolidBrush(Ui.Dim)) g.DrawString("APCA Lc " + Math.Round(lc), Ui.Small, b, r.X + 14, r.Y + 42);
            float x = r.Right - 12;
            foreach (var t in new[] { Tuple.Create("AAA", ratio >= 7), Tuple.Create("AA", ratio >= 4.5), Tuple.Create(L.T("AA groß"), ratio >= 3) })
            {
                float w = g.MeasureString(t.Item1, Badge).Width + 22;
                x -= w;
                var br = new RectangleF(x, r.Y + 21, w, 24);
                Ui.Pill(g, br, t.Item2 ? Color.FromArgb(40, 52, 199, 89) : Color.FromArgb(40, 253, 83, 76), 24);
                var fg = t.Item2 ? Color.FromArgb(120, 225, 150) : Color.FromArgb(255, 140, 130);
                Icons.DrawLucide(g, t.Item2 ? "check" : "x", br.X + 11, br.Y + 12, 11, 1.8f, fg);
                using (var b = new SolidBrush(fg)) g.DrawString(t.Item1, Badge, b, br.X + 18, br.Y + 5);
                x -= 6;
            }
        }

        void DrawFormats(Graphics g)
        {
            var card = new Rectangle(Pad, FormatsY, W - Pad * 2, RowH * Colors.Formats.Length);
            Ui.Pill(g, card, Ui.Card, 18);
            for (int i = 0; i < Colors.Formats.Length; i++)
            {
                var r = RowR(i);
                if (i == hoverRow) using (var path = Renderer.Round(RectangleF.Inflate(r, -4, -3), 12)) using (var b = new SolidBrush(Ui.Well)) g.FillPath(b, path);
                if (i > 0) Ui.Hairline(g, r.X + 12, r.Y, r.Right - 12, r.Y);
                using (var b = new SolidBrush(Ui.Dim)) g.DrawString(Colors.Formats[i].ToUpperInvariant(), Ui.Small, b, r.X + 12, r.Y + 9);
                string value = Colors.Format(slots[active], Colors.Formats[i]);
                using (var b = new SolidBrush(Ui.Text))
                using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(value, Mono, b, new RectangleF(r.X + 64, r.Y, r.Width - 64 - 36, r.Height), sf);
                if (i == copiedRow) Icons.DrawLucide(g, "check", r.Right - 20, r.Y + r.Height / 2f, 14, 2f, Color.FromArgb(52, 199, 89));
                else if (i == hoverRow) Icons.DrawLucide(g, "copy", r.Right - 20, r.Y + r.Height / 2f, 13, 1.5f, Ui.Dim);
            }
        }

        void DrawHistory(Graphics g)
        {
            using (var b = new SolidBrush(Ui.Dim)) g.DrawString(L.T("Verlauf"), Ui.Small, b, Pad - 1, HistoryY);
            var history = History();
            if (history.Count == 0)
                using (var b = new SolidBrush(Ui.Dim)) g.DrawString(L.T("Noch keine Farben"), Ui.Small, b, Pad - 1, HistoryY + 22);
            for (int i = 0; i < history.Count; i++)
            {
                var r = DotR(i);
                using (var b = new SolidBrush(history[i])) g.FillEllipse(b, r);
                using (var p = new Pen(i == hoverHistory ? Ui.Text : Color.FromArgb(50, 255, 255, 255), i == hoverHistory ? 2 : 1)) g.DrawEllipse(p, r);
            }
        }

        protected override void Dispose(bool d) { if (d) copiedTimer.Dispose(); base.Dispose(d); }
    }
}
