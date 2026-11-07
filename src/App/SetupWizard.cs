using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Glint
{
    class PrimaryButton : UiControl
    {
        public string Caption;
        bool down;
        public PrimaryButton(string text, Size size) { Caption = text; Size = size; BackColor = Ui.Bg; }
        static readonly Font Face = new Font("Segoe UI Semibold", 14f, GraphicsUnit.Pixel);
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            down = false; Invalidate();
            if (ClientRectangle.Contains(e.Location)) Fire();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var fill = down ? Color.FromArgb(226, 68, 62) : hot ? Color.FromArgb(255, 104, 97) : Brand.Pixel;
            Ui.Pill(g, new RectangleF(0, 0, LW - 1, LH - 1), fill, 24);
            Ui.Center(g, Caption, Face, Brand.Dark, new RectangleF(0, 0, LW, LH));
        }
    }

    class TextButton : UiControl
    {
        public string Caption;
        public TextButton(string text, Size size) { Caption = text; Size = size; BackColor = Ui.Bg; }
        protected override void OnMouseDown(MouseEventArgs e) { Fire(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            Ui.Center(g, Caption, Ui.Small, hot ? Ui.Text : Ui.Dim, new RectangleF(0, 0, LW, LH));
        }
    }

    class SetupWizard : Form
    {
        const int W = 420, H = 600, Pad = Ui.Pad, RowH = Ui.RowH, RowsTop = 144;
        public bool Relaunch { get; private set; }
        readonly Toggle install = new Toggle(true), startMenu = new Toggle(true), desktop = new Toggle(false), autostart = new Toggle(Installer.Autostart);
        readonly List<Tuple<string, Toggle>> rows;
        readonly PrimaryButton primary;
        readonly TextButton secondary;
        readonly Rectangle closeRect = new Rectangle(W - 42, 0, 42, 36);
        readonly Timer anim = new Timer { Interval = 15 };
        DateTime animStart;
        float t = 1f;
        int step;
        bool failed, hoverClose;

        public SetupWizard()
        {
            Ui.Refresh();
            Text = L.T("Glint einrichten");
            Icon = Brand.AppIcon;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; BackColor = Ui.Bg; Font = Ui.Font; DoubleBuffered = true;
            ClientSize = Ui.S(W, H);
            rows = new List<Tuple<string, Toggle>> {
                Tuple.Create(L.T("In Programme installieren"), install), Tuple.Create(L.T("Eintrag im Startmenü"), startMenu),
                Tuple.Create(L.T("Verknüpfung auf dem Desktop"), desktop), Tuple.Create(L.T("Mit Windows starten"), autostart) };
            for (int i = 0; i < rows.Count; i++)
            {
                var c = rows[i].Item2;
                c.Location = new Point(Ui.S(W - Pad - 14) - c.Width, Ui.S(RowsTop + i * RowH) + (Ui.S(RowH) - c.Height) / 2);
                Controls.Add(c);
            }
            install.Changed = () =>
            {
                startMenu.Enabled = desktop.Enabled = install.On;
                if (!install.On) { startMenu.On = desktop.On = false; startMenu.Invalidate(); desktop.Invalidate(); }
                Invalidate();
            };
            primary = new PrimaryButton("", Ui.S(W - Pad * 2, 48)) { Location = new Point(Ui.S(Pad), Ui.S(H - 92)) };
            primary.Changed = Advance;
            secondary = new TextButton("", Ui.S(W - Pad * 2, 28)) { Location = new Point(Ui.S(Pad), Ui.S(H - 40)) };
            secondary.Changed = () => { if (step == 1) Go(0); else Finish(); };
            Controls.Add(primary); Controls.Add(secondary);
            anim.Tick += delegate
            {
                t = Math.Min(1f, (float)(DateTime.Now - animStart).TotalMilliseconds / 420f);
                Invalidate();
                if (t >= 1f) anim.Stop();
            };
            Go(0);
        }

        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Rounded(Handle); }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Animate(); }
        protected override void Dispose(bool d) { if (d) anim.Dispose(); base.Dispose(d); }

        void Animate() { t = 0f; animStart = DateTime.Now; anim.Start(); }

        public void Go(int i)
        {
            step = i;
            foreach (var r in rows) r.Item2.Visible = i == 1;
            primary.Caption = i == 0 ? L.T("Los geht's") : i == 1 ? L.T("Einrichten") : L.T("Glint starten");
            secondary.Caption = i == 0 ? L.T("Ohne Einrichtung nutzen") : i == 1 ? L.T("Zurück") : "";
            secondary.Visible = i < 2;
            primary.Invalidate(); secondary.Invalidate();
            if (IsHandleCreated && Visible) Animate();
            Invalidate();
        }

        void Advance()
        {
            if (step == 0) { Go(1); return; }
            if (step == 2) { Finish(); return; }
            try
            {
                failed = false;
                if (install.On)
                {
                    Installer.Install(startMenu.On, desktop.On, autostart.On);
                    Relaunch = !Installer.RunningInstalled;
                }
                else Installer.SetAutostart(autostart.On);
            }
            catch (Exception ex)
            {
                Log.Error("Einrichtung", ex);
                failed = true;
                Relaunch = false;
            }
            Go(2);
        }

        void Finish()
        {
            Settings.Current.SetupDone = true;
            Settings.Current.Save();
            Close();
        }

        static void CenterText(Graphics g, string s, Font f, Color c, float y, float h)
        {
            using (var b = new SolidBrush(c))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(s, f, b, new RectangleF(Pad + 8, y, W - (Pad + 8) * 2, h), sf);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float ease = 1f - (float)Math.Pow(2, -10 * t);
            if (step == 0) PaintWelcome(g, ease);
            else if (step == 1) PaintOptions(g);
            else PaintDone(g, ease);

            for (int i = 0; i < 3; i++)
            {
                float x = W / 2f - 24 + i * 24, y = H - 112;
                using (var b = new SolidBrush(i == step ? Brand.Pixel : Ui.Well))
                    if (i == step) g.FillRectangle(b, x - 3.5f, y - 3.5f, 7, 7); else g.FillEllipse(b, x - 3, y - 3, 6, 6);
            }
            if (hoverClose) using (var b = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillRectangle(b, closeRect);
            Icons.DrawLucide(g, "x", closeRect.X + closeRect.Width / 2f, closeRect.Height / 2f, 15, 1.3f, Ui.Dim);
        }

        void PaintWelcome(Graphics g, float ease)
        {
            int size = 112;
            float rise = (1 - ease) * 10, y = 120 + rise, scale = .94f + .06f * ease;
            float s = size * scale;
            using (var logo = Brand.AppBitmap(224))
                g.DrawImage(logo, (W - s) / 2, y + (size - s) / 2, s, s);
            float xh = 30, ww = Brand.WordmarkWidth(xh);
            Brand.DrawWordmark(g, (W - ww) / 2, 280 + rise * .6f, xh, Ui.Text, Brand.Pixel);
            using (var f = new Font("Segoe UI", 16f, GraphicsUnit.Pixel))
                CenterText(g, L.T("Das Pixel, auf das es ankommt."), f, Ui.Text, 346, 28);
            CenterText(g, L.T("Aufnehmen, markieren, anpinnen und teilen. Die Einrichtung dauert nur einen Moment und braucht keine Administratorrechte."), Ui.Small, Ui.Dim, 380, 64);
        }

        void PaintOptions(Graphics g)
        {
            CenterText(g, L.T("Wie soll Glint laufen?"), Ui.Title, Ui.Text, 56, 34);
            CenterText(g, L.T("Das kannst du später jederzeit ändern."), Ui.Small, Ui.Dim, 94, 22);
            Ui.Pill(g, new RectangleF(Pad, RowsTop, W - Pad * 2, rows.Count * RowH), Ui.Card, Ui.CardRadius);
            for (int i = 0; i < rows.Count; i++)
            {
                float y = RowsTop + i * RowH;
                if (i > 0) Ui.Hairline(g, Pad + 14, (int)y, W - Pad, (int)y);
                using (var b = new SolidBrush(rows[i].Item2.Enabled ? Ui.Text : Ui.Dim))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                    g.DrawString(rows[i].Item1, Ui.Font, b, new RectangleF(Pad + 14, y, W - Pad * 2 - 80, RowH), sf);
            }
        }

        void PaintDone(Graphics g, float ease)
        {
            float rise = (1 - ease) * 10;
            using (var logo = Brand.AppBitmap(176)) g.DrawImage(logo, (W - 88) / 2f, 128 + rise, 88, 88);
            using (var f = new Font("Segoe UI Semibold", 22.5f, GraphicsUnit.Pixel))
                CenterText(g, failed ? L.T("Fast geschafft") : L.T("Alles bereit"), f, Ui.Text, 244, 40);
            string text = failed ? L.T("Die Installation hat nicht geklappt, Details stehen im Log. Glint läuft trotzdem aus dem aktuellen Ordner.")
                : install.On ? L.T("Glint wartet im Infobereich der Taskleiste.") + (autostart.On ? L.T(" Es startet ab jetzt mit Windows.") : "")
                : L.T("Glint läuft aus dem aktuellen Ordner und wartet im Infobereich der Taskleiste.");
            CenterText(g, text, Ui.Font, Ui.Dim, 288, 56);
            using (var f = new Font("Segoe UI Semibold", 14.5f, GraphicsUnit.Pixel))
            {
                string key = Settings.Pretty(Settings.Current.Hotkeys["Area"]);
                string hint = L.T("startet eine Aufnahme");
                float kw = Math.Max(52, g.MeasureString(key, f).Width + 20), hw = g.MeasureString(hint, Ui.Font).Width;
                float x = (W - kw - 10 - hw) / 2, y = 360;
                Ui.Pill(g, new RectangleF(x, y, kw, 34), Ui.Well, 14);
                Ui.Center(g, key, f, Ui.Text, new RectangleF(x, y, kw, 34));
                using (var b = new SolidBrush(Ui.Text))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                    g.DrawString(hint, Ui.Font, b, new RectangleF(x + kw + 10, y, hw + 4, 34), sf);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = new IntPtr(closeRect.Contains(new Point((int)(p.X / Ui.F), (int)(p.Y / Ui.F))) ? 1 : 2);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool c = closeRect.Contains(new Point((int)(e.X / Ui.F), (int)(e.Y / Ui.F)));
            if (c != hoverClose) { hoverClose = c; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hoverClose = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (closeRect.Contains(new Point((int)(e.X / Ui.F), (int)(e.Y / Ui.F)))) Finish(); }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Enter) { Advance(); return true; }
            if (k == Keys.Escape) { if (step == 1) Go(0); else Finish(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
    }
}
