using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glint
{
    class FloatingPreview : Glass, IImageSource
    {
        static readonly List<FloatingPreview> open = new List<FloatingPreview>();
        static readonly string[] Ids = { "edit", "copy", "save", "upload", "ocr", "pin" };
        static readonly string[] Icon = { "pencil", "copy", "image", "cloud-upload", "scan-text", "pin" };
        readonly string[] Tips = { "Editor", L.T("Kopieren"), L.T("Speichern"), L.T("Hochladen"), L.T("Text erkennen"), L.T("Anpinnen") };
        static readonly Color IconColor = Color.FromArgb(226, 227, 230);
        const int MarginBase = 8, MaxImgWBase = 284, MaxImgHBase = 190, RowHBase = 42, SpacingBase = 38, ButtonBase = 32;
        readonly float k;

        // Card width as a share of the screen, kept within sensible limits.
        public static float ScaleFor(int screenWidth) { return Math.Max(220f, Math.Min(460f, screenWidth * .125f)) / 300f; }
        int K(float v) { return (int)Math.Round(v * k); }
        readonly Bitmap img, thumb;
        readonly Rectangle card, imgRect, closeRect, dragRect;
        readonly Rectangle[] rects = new Rectangle[Ids.Length];
        readonly Timer poll;
        int hover = -1; bool hoverClose, hoverDrag, inside;
        DragGhost ghost; Rectangle homeRect;
        Point? dragFrom;
        string dragFile;
        bool dropped, handedOff; // handedOff: another window or OCR owns the image now

        public FloatingPreview(Bitmap b)
        {
            img = b;
            KeyPreview = true;
            var wa = (Settings.Current.PreviewOnMain ? Screen.PrimaryScreen : Screen.FromPoint(Cursor.Position)).WorkingArea;
            k = ScaleFor(wa.Width);
            int margin = K(MarginBase), maxW = K(MaxImgWBase), maxH = K(MaxImgHBase), rowH = K(RowHBase), spacing = K(SpacingBase), button = K(ButtonBase);
            float s = Math.Min(1f, Math.Min((float)maxW / b.Width, (float)maxH / b.Height));
            int iw = Math.Max(1, (int)(b.Width * s)), ih = Math.Max(1, (int)(b.Height * s));
            // The card hugs the image, it only stays wider than the image to fit the buttons.
            int cardW = Math.Max(iw + margin * 2, spacing * Ids.Length + margin * 2), cardH = margin + ih + rowH;
            card = new Rectangle(S, S, cardW, cardH);
            ClientSize = new Size(cardW + S * 2, cardH + S * 2);
            imgRect = new Rectangle(S + (cardW - iw) / 2, S + margin, iw, ih);
            thumb = new Bitmap(iw, ih, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(b, 0, 0, iw, ih);
            }
            closeRect = new Rectangle(imgRect.Right - K(24), imgRect.Y + K(5), K(19), K(19));
            dragRect = new Rectangle(imgRect.X + K(5), imgRect.Y + K(5), K(19), K(19));
            for (int i = 0; i < rects.Length; i++)
                rects[i] = new Rectangle(S + cardW / 2 + (int)((i - (rects.Length - 1) / 2f) * spacing) - button / 2, S + margin + ih + (rowH - button) / 2, button, button);
            int bottom = wa.Bottom;
            foreach (var f in open) if (f.Bounds.IntersectsWith(wa) && f.Left + f.Width > wa.Right - Width - 40) bottom = Math.Min(bottom, f.Top + S);
            Location = new Point(wa.Right - Width, Math.Max(wa.Top, bottom - Height));
            open.Add(this);
            poll = new Timer { Interval = 100 };
            poll.Tick += delegate
            {
                bool now = Bounds.Contains(Cursor.Position);
                if (now != inside) { inside = now; Invalidate(); }
            };
            poll.Start();
        }

        protected override Rectangle PanelRect { get { return card; } }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            poll.Dispose();
            open.Remove(this);
            if (dragFile != null && !dropped)
                try { File.Delete(dragFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            base.OnFormClosed(e);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
        public void TestHover(int i) { hover = i; }

        public override void PaintTo(Graphics g)
        {
            DrawPanel(g, card, K(26));
            using (var path = Renderer.Round(imgRect, K(14)))
            {
                var st = g.Save();
                g.SetClip(path);
                g.DrawImageUnscaled(thumb, imgRect.X, imgRect.Y);
                g.Restore(st);
            }
            for (int i = 0; i < rects.Length; i++)
            {
                if (i == hover)
                    using (var path = Renderer.Round(rects[i], K(14))) using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) g.FillPath(b, path);
                Icons.DrawLucide(g, Icon[i], rects[i].X + rects[i].Width / 2f, rects[i].Y + rects[i].Height / 2f, K(17), Math.Max(1.2f, k * 1.35f), IconColor);
            }
            if (hoverDrag) Chip.Draw(g, L.T("In andere App ziehen"), imgRect);
            else if (hover >= 0 || hoverClose) Chip.Draw(g, hoverClose ? L.T("Schließen") : Tips[hover], imgRect, KeyOf(hoverClose ? "close" : Ids[hover]));
            if (inside)
            {
                Ui.Disc(g, dragRect.X, dragRect.Y, dragRect.Width, hoverDrag ? Color.FromArgb(235, 60, 60, 65) : Color.FromArgb(170, 28, 28, 28));
                Icons.DrawLucide(g, "grip-vertical", dragRect.X + dragRect.Width / 2f, dragRect.Y + dragRect.Height / 2f, K(13), Math.Max(1.1f, k * 1.25f), Color.White);
                Ui.Disc(g, closeRect.X, closeRect.Y, closeRect.Width, hoverClose ? Color.FromArgb(253, 83, 76) : Color.FromArgb(170, 28, 28, 28));
                Icons.DrawLucide(g, "x", closeRect.X + closeRect.Width / 2f, closeRect.Y + closeRect.Height / 2f, K(13), Math.Max(1.1f, k * 1.25f), Color.White);
            }
        }

        // Key press outside Glint: returns what to run for the newest window, or null.
        public static Action GlobalKey(Keys combo)
        {
            if (open.Count == 0 || !Settings.Current.GlobalKeys || Form.ActiveForm != null) return null;
            foreach (var f in open) if (f.ghost != null) return null; // Esc has to reach the running drag
            var id = ActionFor(combo);
            if (id == null) return null;
            if (combo != Keys.Escape && Native.CaretActive()) return null; // don't steal typing
            var top = open[open.Count - 1];
            return () => { if (!top.IsDisposed) top.Run(id); };
        }

        static string KeyOf(string id)
        {
            string k;
            return Settings.Current.PreviewKeys.TryGetValue(id, out k) && !string.IsNullOrEmpty(k) ? Settings.Pretty(k) : null;
        }

        public Bitmap CurrentImage() { return img; }

        void StartDrag()
        {
            if (dragFile == null) dragFile = Imaging.SaveFile(img, Imaging.TempFile(".png"));
            var result = DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { dragFile }), DragDropEffects.Copy);
            dropped = result != DragDropEffects.None;
            var g = ghost;
            if (g == null) return; // plain click, the drag never started
            if (result != DragDropEffects.None) g.Drop(() => { ghost = null; g.Close(); Close(); });
            else g.Cancel(homeRect, () => { ghost = null; Visible = true; g.Close(); });
        }

        // Fires once the drag has really started and on every mouse move after that.
        protected override void OnGiveFeedback(GiveFeedbackEventArgs e)
        {
            if (ghost == null)
            {
                homeRect = RectangleToScreen(imgRect);
                float s = Math.Min(1f, Math.Min((float)K(140) / thumb.Width, (float)K(100) / thumb.Height));
                ghost = new DragGhost(thumb, homeRect, new Size(Math.Max(1, (int)(thumb.Width * s)), Math.Max(1, (int)(thumb.Height * s))));
                ghost.Show();
                hoverDrag = false;
                Visible = false;
            }
            ghost.Tick();
            e.UseDefaultCursors = false;
            Cursor.Current = e.Effect == DragDropEffects.None ? Cursors.No : Cursors.Hand;
            base.OnGiveFeedback(e);
        }

        void Run(string id)
        {
            if (ghost != null) return;
            switch (id)
            {
                case "edit": handedOff = true; new Editor(img).Show(); Close(); break;
                case "copy": Imaging.Copy(img); Toast.Show(L.T("In die Zwischenablage kopiert")); Close(); break;
                case "save": Toast.Show(L.T("Gespeichert: ") + Path.GetFileName(Imaging.QuickSave(img))); Close(); break;
                case "upload": Uploader.RunActive(img); Close(); break;
                case "ocr": handedOff = true; Tray.Instance.OcrAndCopy(img); Close(); break;
                case "pin": handedOff = true; new PinWindow(img).Show(); Close(); break;
                case "close": Close(); break;
            }
        }

        public static string ActionFor(Keys k)
        {
            foreach (var p in Settings.Current.PreviewKeys)
            {
                Keys parsed;
                if (Settings.ParseKeys(p.Value, out parsed) && parsed == k) return p.Key;
            }
            return null;
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            var a = ActionFor(k);
            if (a != null) { Run(a); return true; }
            return base.ProcessCmdKey(ref m, k);
        }

        bool IsControl(Point p)
        {
            if (inside && (closeRect.Contains(p) || dragRect.Contains(p))) return true;
            foreach (var r in rects) if (r.Contains(p)) return true;
            return false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) // native move everywhere except the buttons
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = new IntPtr(IsControl(p) || ((ModifierKeys & Keys.Control) != 0 && imgRect.Contains(p)) ? 1 : 2);
                return;
            }
            if (m.Msg == 0x20) // WM_SETCURSOR: move cursor wherever a drag would start
            {
                var c = PointToClient(Cursor.Position);
                bool caption = (m.LParam.ToInt64() & 0xFFFF) == 2;
                bool grab = (inside && dragRect.Contains(c)) || ((ModifierKeys & Keys.Control) != 0 && imgRect.Contains(c));
                if (caption || grab)
                {
                    Cursor.Current = grab ? Cursors.Hand : Cursors.SizeAll;
                    m.Result = new IntPtr(1);
                    return;
                }
            }
            if (m.Msg == 0xA3) { Run("edit"); return; } // double click
            base.WndProc(ref m);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && rects[Array.IndexOf(Ids, "upload")].Contains(e.Location)) { Uploader.ShowMenu(() => img); return; }
            if (e.Button != MouseButtons.Left) return;
            if (inside && closeRect.Contains(e.Location)) { Close(); return; }
            if (dragRect.Contains(e.Location) || ((ModifierKeys & Keys.Control) != 0 && imgRect.Contains(e.Location))) { dragFrom = e.Location; return; }
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(e.Location)) { Run(Ids[i]); return; }
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragFrom = null; base.OnMouseUp(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // Encoding waits for a real drag, a click on the grip costs nothing.
            if (dragFrom.HasValue && e.Button == MouseButtons.Left)
            {
                var area = new Rectangle(dragFrom.Value, SystemInformation.DragSize);
                area.Offset(-area.Width / 2, -area.Height / 2);
                if (!area.Contains(e.Location)) { dragFrom = null; StartDrag(); return; }
            }
            int h = -1;
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(e.Location)) h = i;
            bool hc = closeRect.Contains(e.Location);
            bool hd = dragRect.Contains(e.Location);
            if (h != hover || hc != hoverClose || hd != hoverDrag) { hover = h; hoverClose = hc; hoverDrag = hd; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverClose = false; hoverDrag = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void Dispose(bool d) { if (d) { thumb.Dispose(); if (!handedOff) img.Dispose(); } base.Dispose(d); }
    }
}
