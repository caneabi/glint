using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinOcr = Windows.Media.Ocr;
using WinImg = Windows.Graphics.Imaging;

namespace Glint
{
    class Toast : Form
    {
        static Toast current;
        readonly string text;
        readonly bool isError;
        Action onClick;
        Toast(string text, int ms, bool error)
        {
            this.text = text; isError = error;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Ui.Side; DoubleBuffered = true;
            if (error) Cursor = Cursors.Hand;
            using (var f = new Font("Segoe UI", 10f))
            using (var g = CreateGraphics())
            {
                var s = g.MeasureString(text, f);
                Size = new Size((int)s.Width + 28 + 22, 38);
            }
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - 18, wa.Bottom - Height - 18);
            using (var p = new GraphicsPath())
            {
                p.AddArc(0, 0, 14, 14, 180, 90); p.AddArc(Width - 15, 0, 14, 14, 270, 90);
                p.AddArc(Width - 15, Height - 15, 14, 14, 0, 90); p.AddArc(0, Height - 15, 14, 14, 90, 90); p.CloseFigure();
                Region = new Region(p);
            }
            var t = new Timer { Interval = ms };
            t.Tick += delegate { t.Dispose(); Close(); };
            t.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var c = base.CreateParams; c.ExStyle |= 0x08000000 | 0x80; return c; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int x = 35;
            if (!isError) Brand.DrawMark(g, new Rectangle(13, 12, 14, 14), Color.FromArgb(232, 232, 235), Brand.Pixel);
            else
            {
                using (var b = new SolidBrush(Color.FromArgb(253, 83, 76))) g.FillEllipse(b, 13, 12, 14, 14);
                using (var p = new Pen(Color.White, 1.6f)) { g.DrawLine(p, 20, 15, 20, 20); g.DrawLine(p, 20, 22.5f, 20, 23); }
            }
            using (var f = new Font("Segoe UI", 10f)) g.DrawString(text, f, Brushes.White, x, 9);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (onClick != null) onClick();
            else if (isError) Log.Open();
            else return;
            Close();
        }
        static void Present(string text, int ms, bool error, Action click = null)
        {
            // Settings and its dialogs show notifications inside the window.
            var host = Form.ActiveForm as ResizableForm;
            if (host != null) { host.ShowBanner(text, ms, error, click); return; }
            if (current != null && !current.IsDisposed) current.Close();
            current = new Toast(text, ms, error);
            if (click != null) { current.onClick = click; current.Cursor = Cursors.Hand; }
            ((Form)current).Show();
        }
        public static void Show(string text, int ms = 1600) { Present(text, ms, false); }
        public static void Action(string text, int ms, Action click) { Present(text, ms, false, click); }
        public static void Error(string text, string context, Exception ex)
        {
            Log.Error(context, ex);
            Present(text, 4000, true);
        }
    }
}
