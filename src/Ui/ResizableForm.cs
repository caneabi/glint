using System;
using System.Drawing;
using System.Windows.Forms;

namespace Glint
{
    // In-window notification.
    class Banner : UiControl
    {
        static readonly Font Face = new Font("Segoe UI", 12.5f, GraphicsUnit.Pixel);
        readonly string text;
        readonly bool isError;
        readonly Action click;

        public Banner(string text, bool isError, Action click)
        {
            this.text = text; this.isError = isError; this.click = click;
            BackColor = Ui.Bg;
            Cursor = click != null || isError ? Cursors.Hand : Cursors.Default;
            int w = TextRenderer.MeasureText(text, new Font("Segoe UI", 12.5f * Ui.F, GraphicsUnit.Pixel)).Width;
            Size = new Size(w + Ui.S(52), Ui.S(38));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (click != null) click();
            else if (isError) Log.Open();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e);
            var r = new RectangleF(.5f, .5f, LW - 1, LH - 1);
            Ui.Pill(g, r, Ui.Side, 20);
            using (var path = Renderer.Round(r, 20)) using (var p = new Pen(isError ? Ui.Accent : Ui.Well)) g.DrawPath(p, path);
            if (isError)
            {
                using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, 13, LH / 2 - 7, 14, 14);
                using (var p = new Pen(Color.White, 1.6f))
                {
                    g.DrawLine(p, 20, LH / 2 - 4, 20, LH / 2 + 1);
                    g.DrawLine(p, 20, LH / 2 + 3.5f, 20, LH / 2 + 4);
                }
            }
            else Icons.DrawLucide(g, "check", 20, LH / 2, 15, 1.8f, Ui.Accent);
            using (var b = new SolidBrush(Ui.Text))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text, Face, b, new RectangleF(35, 0, LW - 40, LH), sf);
        }
    }

    // Empty areas belong to the window, so it can still be dragged and resized there.
    class PassPanel : Panel
    {
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public PassPanel() { BackColor = Ui.Bg; DoubleBuffered = true; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetWindowTheme(Handle, "DarkMode_Explorer", null); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && m.Result.ToInt64() == 1) m.Result = new IntPtr(-1);
        }
    }

    // Borderless window that can be resized from its edges.
    class ResizableForm : Form
    {
        // Fits inside the settings window, whatever the modal contains.
        const int ModalW = 760, ModalH = 640;

        protected void FitModal()
        {
            MinimumSize = Ui.S(ModalW, ModalH);
            FitToScreen(ModalW, ModalH);
        }

        public ResizableForm()
        {
            Ui.Refresh();
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Ui.Bg; ForeColor = Ui.Text; DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected void FitToScreen(int w, int h)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            ClientSize = new Size(Math.Min(Ui.S(w), wa.Width), Math.Min(Ui.S(h), wa.Height));
        }

        protected virtual bool IsControlAt(Point p) { return false; }

        Banner banner;
        Timer bannerTimer;
        public Banner ActiveBanner { get { return banner; } }

        public void ShowBanner(string text, int ms, bool error, Action click)
        {
            HideBanner();
            banner = new Banner(text, error, click);
            Controls.Add(banner);
            PlaceBanner();
            banner.BringToFront();
            bannerTimer = new Timer { Interval = ms };
            bannerTimer.Tick += delegate { HideBanner(); };
            bannerTimer.Start();
        }

        void HideBanner()
        {
            if (bannerTimer != null) { bannerTimer.Dispose(); bannerTimer = null; }
            if (banner != null) { var b = banner; banner = null; Controls.Remove(b); b.Dispose(); }
        }

        void PlaceBanner()
        {
            if (banner != null) banner.Location = new Point((ClientSize.Width - banner.Width) / 2, ClientSize.Height - banner.Height - Ui.S(14));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); PlaceBanner(); }
        protected override void Dispose(bool d) { if (d) HideBanner(); base.Dispose(d); }

        public int HitAt(Point p)
        {
            int e = Ui.S(6), w = ClientSize.Width, h = ClientSize.Height;
            bool l = p.X < e, r = p.X >= w - e, t = p.Y < e, b = p.Y >= h - e;
            if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
            if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            return IsControlAt(p) ? 1 : 2;
        }

        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ClassStyle |= 0x20000; return c; } } // CS_DROPSHADOW
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Rounded(Handle); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84)
            {
                long lp = m.LParam.ToInt64();
                m.Result = new IntPtr(HitAt(PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)))));
                return;
            }
            base.WndProc(ref m);
        }
    }
}
