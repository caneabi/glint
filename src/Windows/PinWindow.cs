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
    class PinWindow : Form, IImageSource
    {
        readonly Bitmap img;
        Bitmap view; // cached at the current zoom
        float scale = 1f;
        readonly PinBar bar;
        readonly Timer hoverTimer;
        string tip, tipKey;
        public bool KeepBar; // test hook

        public PinWindow(Bitmap b)
        {
            img = b;
            FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            scale = Math.Min(1f, Math.Min(wa.Width * .9f / b.Width, wa.Height * .9f / b.Height));
            ClientSize = new Size(Math.Max(1, (int)(b.Width * scale)), Math.Max(1, (int)(b.Height * scale)));
            Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);
            var m = new ContextMenuStrip();
            MenuKit.Style(m, false);
            m.Items.Add(L.T("Kopieren"), null, delegate { Imaging.Copy(img); });
            m.Items.Add(L.T("In Editor öffnen"), null, delegate { OpenEditor(); });
            foreach (var o in new[] { 100, 75, 50, 25 }) { int v = o; m.Items.Add(L.T("Deckkraft ") + v + " %", null, delegate { Opacity = v / 100.0; }); }
            m.Items.Add(L.T("Schließen"), null, delegate { Close(); });
            ContextMenuStrip = m;
            bar = new PinBar(this);
            hoverTimer = new Timer { Interval = 80 };
            hoverTimer.Tick += delegate { SyncBar(); };
            hoverTimer.Start();
        }

        public Bitmap Image { get { return img; } }
        public Bitmap CurrentImage() { return img; }
        public int Percent { get { return (int)Math.Round(scale * 100); } }
        public void SetTip(string t, string key) { tip = t; tipKey = key; Invalidate(); }
        public void CopyImage() { Imaging.Copy(img); Toast.Show(L.T("In die Zwischenablage kopiert")); }
        Point BarPos { get { return new Point(Right - 10 - bar.Width + PinBar.Margin2, Top + 10 - PinBar.Margin2); } }

        void SyncBar()
        {
            var c = Cursor.Position;
            bool show = KeepBar || (Visible && (Bounds.Contains(c) || (bar.Visible && bar.Bounds.Contains(c))));
            if (show && !bar.Visible) { bar.Location = BarPos; bar.Show(this); }
            else if (!show && bar.Visible) { bar.Hide(); SetTip(null, null); }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Native.Rounded(Handle); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (bar != null && bar.Visible) bar.Location = BarPos; }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (bar != null && bar.Visible) bar.Location = BarPos; }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x84: m.Result = new IntPtr(2); return; // let Windows do the moving
                case 0xA3: Close(); return; // double click
                case 0xA5: ContextMenuStrip.Show(Cursor.Position); return; // right click
                case 0x232: base.WndProc(ref m); if (bar.Visible) bar.RefreshBackdrop(); return;
            }
            base.WndProc(ref m);
        }

        void ApplyZoom()
        {
            if (view != null) { view.Dispose(); view = null; }
            ClientSize = new Size(Math.Max(1, (int)(img.Width * scale)), Math.Max(1, (int)(img.Height * scale)));
            Invalidate(); bar.Invalidate();
        }

        public void ResetZoom() { scale = 1; ApplyZoom(); }
        public void OpenEditor() { new Editor(Imaging.Normalize(img)).Show(); Close(); }
        public void DragFile(Control from)
        {
            var path = Imaging.TempFile(".png");
            Imaging.SaveFile(img, path);
            from.DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            hoverTimer.Dispose();
            bar.Close();
            base.OnFormClosed(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (view == null || view.Size != ClientSize)
            {
                if (view != null) view.Dispose();
                view = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(view))
                {
                    g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(img, 0, 0, view.Width, view.Height);
                }
            }
            e.Graphics.CompositingMode = CompositingMode.SourceCopy;
            e.Graphics.DrawImageUnscaled(view, 0, 0);
            e.Graphics.CompositingMode = CompositingMode.SourceOver;
            if (tip != null) Chip.Draw(e.Graphics, tip, ClientRectangle, tipKey);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scale = Math.Max(.1f, Math.Min(6f, scale * (e.Delta > 0 ? 1.1f : .9f)));
            ApplyZoom();
        }
        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            switch (k)
            {
                case Keys.Escape: Close(); return true;
                case Keys.E: OpenEditor(); return true;
                case Keys.U: Uploader.RunActive(img); return true;
                case Keys.Control | Keys.C: CopyImage(); return true;
                case Keys.D0: case Keys.NumPad0: ResetZoom(); return true;
            }
            return base.ProcessCmdKey(ref m, k);
        }
        protected override void Dispose(bool d) { if (d) { if (view != null) { view.Dispose(); view = null; } img.Dispose(); } base.Dispose(d); }
    }
}
