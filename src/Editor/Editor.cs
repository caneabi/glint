using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Glint
{
    public class Editor : Form, IImageSource
    {
        const int BarH = 52, AttrH = 0, StatusH = 0;
        static readonly Color Bg = Ui.Bg, Bar = Ui.Side, Hover = Ui.Well, Accent = Ui.Accent;
        static readonly Color[] Palette = { Color.FromArgb(255, 94, 91), Color.FromArgb(255, 170, 40), Color.FromArgb(255, 221, 50), Color.FromArgb(70, 200, 110), Color.FromArgb(60, 140, 255), Color.FromArgb(170, 100, 245), Color.White, Color.Black };

        Bitmap baseImg, composed, display;
        public List<Obj> Objs = new List<Obj>();
        readonly Stack<Tuple<Bitmap, List<Obj>>> undo = new Stack<Tuple<Bitmap, List<Obj>>>(), redo = new Stack<Tuple<Bitmap, List<Obj>>>();
        Tool tool = Tool.Select;
        Obj sel, draft;
        Color color = Palette[0]; float width = 4; bool fill, hand; int opacity = 100;
        int backdrop = -1; const int Pad = 64;
        float zoom = 1; bool fit = true; PointF pan = PointF.Empty;
        readonly List<Btn> buttons = new List<Btn>(), attrButtons = new List<Btn>();
        Btn hover;
        Rectangle sizeRect, colorRect, zoomRect, attrPanel, morePanel, minR, maxR, closeR;
        bool moreOpen;
        readonly List<int> separators = new List<int>();
        TextureBrush checker;
        int winHover; // 1 min, 2 max, 3 close
        bool Zoomed { get { return IsHandleCreated && Native.IsZoomed(Handle); } }
        Rectangle? crop;
        int handle = -1; bool dragging, panning; PointF last, downAt; Point panStart;
        Point hoverPx; bool hoverValid;
        Color? picked, prevPicked;
        int rulerAxis; TextBox editor; Obj editing; bool editingNew;
        string statusNote = "";
        readonly Dictionary<string, ButtonFx> fx = new Dictionary<string, ButtonFx>();
        Timer fxTimer;
        string note;
        DateTime noteUntil;

        public Editor(Bitmap img)
        {
            baseImg = img;
            Text = "Glint";
            Icon = Brand.AppIcon;
            BackColor = Bg; DoubleBuffered = true; KeyPreview = true; AllowDrop = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int minW = 1000, minH = 560;
            int w = Math.Min(wa.Width, Math.Max(minW, img.Width)), h = Math.Min(wa.Height, Math.Max(minH, img.Height + BarH));
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h);
            MinimumSize = new Size(860, 420);
            BuildButtons();
            Recompose();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            var m = new Native.MARGINS { T = 1 }; // keeps shadow and rounded corners
            Native.DwmExtendFrameIntoClientArea(Handle, ref m);
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27); // SWP_FRAMECHANGED
        }

        int HitTestAt(Point p)
        {
            if (!Zoomed)
            {
                const int E = 6; int w = ClientSize.Width, h = ClientSize.Height;
                bool l = p.X < E, r = p.X >= w - E, t = p.Y < E, b = p.Y >= h - E;
                if (t && l) return 13; if (t && r) return 14; if (b && l) return 16; if (b && r) return 17;
                if (l) return 10; if (r) return 11; if (t) return 12; if (b) return 15;
            }
            if (maxR.Contains(p)) return 9; // HTMAXBUTTON, needed for Win11 snap layouts
            if (p.Y < BarH && HitBtn(p) == null && !sizeRect.Contains(p) && !zoomRect.Contains(p) && !minR.Contains(p) && !closeR.Contains(p)) return 2;
            return 1;
        }

        void SetWinHover(int v) { if (winHover != v) { winHover = v; Invalidate(new Rectangle(minR.X, 0, 138, BarH)); } }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x231: sizing = true; break;
                case 0x232: sizing = false; Invalidate(); break;
                case 0x83: // WM_NCCALCSIZE
                    if (m.WParam != IntPtr.Zero)
                    {
                        if (Native.IsZoomed(Handle))
                        {
                            var r = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                            int f = Native.GetSystemMetrics(32) + Native.GetSystemMetrics(92), v = Native.GetSystemMetrics(33) + Native.GetSystemMetrics(92);
                            r.L += f; r.R -= f; r.T += v; r.B -= v;
                            System.Runtime.InteropServices.Marshal.StructureToPtr(r, m.LParam, false);
                        }
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case 0x84: // WM_NCHITTEST
                    {
                        long lp = m.LParam.ToInt64();
                        m.Result = new IntPtr(HitTestAt(PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)))));
                        return;
                    }
                case 0xA0: // WM_NCMOUSEMOVE
                    SetWinHover(m.WParam.ToInt32() == 9 ? 2 : 0);
                    var tme = new Native.TRACKMOUSEEVENT { Size = 24, Flags = 0x12, Hwnd = Handle };
                    tme.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.TRACKMOUSEEVENT));
                    Native.TrackMouseEvent(ref tme);
                    break;
                case 0x2A2: SetWinHover(0); break; // WM_NCMOUSELEAVE
                case 0xA1: if (m.WParam.ToInt32() == 9) { m.Result = IntPtr.Zero; return; } break;
                case 0xA2:
                    if (m.WParam.ToInt32() == 9) { WindowState = Zoomed ? FormWindowState.Normal : FormWindowState.Maximized; m.Result = IntPtr.Zero; return; }
                    break;
            }
            base.WndProc(ref m);
        }

        void PaintWindowButtons(Graphics g)
        {
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.None;
            if (winHover > 0)
                using (var b = new SolidBrush(winHover == 3 ? Color.FromArgb(232, 17, 35) : Hover))
                    g.FillRectangle(b, winHover == 1 ? minR : winHover == 2 ? maxR : closeR);
            using (var p = new Pen(Color.FromArgb(235, 235, 238)))
            {
                int cy = BarH / 2;
                int x = minR.X + 18; g.DrawLine(p, x, cy, x + 10, cy);
                x = maxR.X + 18;
                if (Zoomed) { g.DrawRectangle(p, x, cy - 3, 8, 8); g.DrawLines(p, new[] { new Point(x + 2, cy - 3), new Point(x + 2, cy - 5), new Point(x + 10, cy - 5), new Point(x + 10, cy + 3), new Point(x + 8, cy + 3) }); }
                else g.DrawRectangle(p, x, cy - 5, 10, 10);
                x = closeR.X + 18;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(p, x, cy - 5, x + 10, cy + 5); g.DrawLine(p, x + 10, cy - 5, x, cy + 5);
            }
            g.SmoothingMode = old;
        }

        void BuildButtons()
        {
            Action<string, string, Tool?> add = (id, tip, t) => buttons.Add(new Btn { Id = id, Tip = tip, Tool = t });
            add("copy", L.T("Kopieren  Strg+C"), null); add("save", L.T("Speichern  Strg+S"), null); add("pin", L.T("Anpinnen  Strg+P"), null); add("upload", L.T("Hochladen  Strg+U"), null); add("ocr", L.T("Text erkennen  Strg+T"), null);
            add("drag", L.T("In andere App ziehen"), null);
            add("select", L.T("Auswählen  V"), Tool.Select); add("arrow", L.T("Pfeil  A"), Tool.Arrow); add("text", "Text  T", Tool.Text); add("ruler", L.T("Lineal  U"), Tool.Ruler);
            add("rect", L.T("Rechteck  R"), Tool.Rect); add("blur", L.T("Weichzeichnen  B"), Tool.Blur); add("pen", L.T("Freihand  P"), Tool.Pen);
            add("crop", L.T("Zuschneiden  X"), Tool.Crop); add("oval", "Oval  O", Tool.Oval); add("line", L.T("Linie  L"), Tool.Line); add("step", L.T("Schrittzähler  N"), Tool.Step);
            add("highlight", L.T("Textmarker  H"), Tool.Highlight); add("spot", "Spotlight  S", Tool.Spotlight); add("mag", L.T("Lupe  M"), Tool.Magnifier);
            add("pixel", L.T("Verpixeln  K"), Tool.Pixelate); add("erase", L.T("Entfernen  E"), Tool.Erase);
            add("undo", L.T("Rückgängig  Strg+Z"), null); add("redo", L.T("Wiederholen  Strg+Y"), null); add("backdrop", "Backdrop", null); add("addcap", L.T("Aufnahme hinzufügen"), null);
            buttons.Add(new Btn { Id = "more", Tip = L.T("Weitere Werkzeuge") });
        }

        bool AttrVisible { get { return sel != null || (tool != Tool.Select && tool != Tool.Crop && tool != Tool.Ruler); } }

        void Layout2()
        {
            int cw = ClientSize.Width;
            closeR = new Rectangle(cw - 46, 0, 46, BarH); maxR = new Rectangle(cw - 92, 0, 46, BarH); minR = new Rectangle(cw - 138, 0, 46, BarH);
            zoomRect = new Rectangle(minR.X - 64, 8, 58, 36);
            sizeRect = new Rectangle(zoomRect.X - 104, 8, 96, 36);
            colorRect = new Rectangle(sizeRect.X - 118, 8, 110, 36);
            int rightStart = colorRect.X - 8, x = 42;
            const int W = 32, H = 30, Y = (BarH - H) / 2;
            separators.Clear();
            var more = buttons[buttons.Count - 1];
            var toolBtns = new List<Btn>();
            foreach (var b in buttons)
            {
                if (b == more) continue;
                if (b.Id == "copy" || b.Id == "save" || b.Id == "pin" || b.Id == "upload")
                {
                    b.R = new Rectangle(x, Y, W, H); x += W + 8;
                    separators.Add(x - 4);
                }
                else if (b.Id == "ocr") { b.R = new Rectangle(x, Y, W, H); x += W + 14; }
                else if (b.Id == "drag") { b.R = new Rectangle(x, Y, 58, H); x += 58 + 14; }
                else toolBtns.Add(b);
            }
            int fit = toolBtns.Count;
            if (x + fit * W > rightStart) fit = Math.Max(0, (rightStart - x - W) / W);
            for (int i = 0; i < fit; i++) { toolBtns[i].R = new Rectangle(x, Y, W, H); x += W; }
            more.R = fit < toolBtns.Count ? new Rectangle(x, Y, W, H) : Rectangle.Empty;
            int over = toolBtns.Count - fit;
            morePanel = Rectangle.Empty;
            if (over > 0 && moreOpen)
            {
                const int Cols = 5, Cell = 36;
                int rows = (over + Cols - 1) / Cols, pw = Math.Min(over, Cols) * Cell + 12, ph = rows * Cell + 12;
                morePanel = new Rectangle(Math.Max(8, more.R.Right - pw), BarH + 6, pw, ph);
                for (int i = 0; i < over; i++)
                    toolBtns[fit + i].R = new Rectangle(morePanel.X + 6 + (i % Cols) * Cell + 2, morePanel.Y + 6 + (i / Cols) * Cell + 3, W, H);
            }
            else for (int i = fit; i < toolBtns.Count; i++) toolBtns[i].R = Rectangle.Empty;

            attrButtons.Clear();
            attrPanel = Rectangle.Empty;
            if (AttrVisible)
            {
                int pw = Palette.Length * 24 + 24 + 16 + 26 * 2 + 44 + 8 + 66 * 2 + 16 + 26 * 2 + 44 + 26;
                attrPanel = new Rectangle((cw - pw) / 2, BarH + 8, pw, 40);
                int ax = attrPanel.X + 13, ay = attrPanel.Y;
                for (int i = 0; i < Palette.Length; i++) { attrButtons.Add(new Btn { Id = "c" + i, Tip = L.T("Farbe"), R = new Rectangle(ax, ay + 11, 18, 18) }); ax += 24; }
                attrButtons.Add(new Btn { Id = "cc", Tip = L.T("Eigene Farbe"), R = new Rectangle(ax, ay + 11, 18, 18) }); ax += 24 + 16;
                foreach (var t in new[] { L.T("w-|Dünner"), L.T("w+|Dicker"), L.T("fill|Füllung"), L.T("hand|Handgezeichnet"), L.T("o-|Weniger Deckkraft"), L.T("o+|Mehr Deckkraft") })
                {
                    var p = t.Split('|');
                    int w = p[0] == "fill" || p[0] == "hand" ? 62 : 24;
                    attrButtons.Add(new Btn { Id = p[0], Tip = p[1], R = new Rectangle(ax, ay + 8, w, 24) });
                    ax += w + 4;
                    if (p[0] == "w+" || p[0] == "o+") ax += 44;
                    if (p[0] == "hand") ax += 12;
                }
            }
        }

        Rectangle CanvasArea { get { return new Rectangle(0, BarH + AttrH, ClientSize.Width, Math.Max(1, ClientSize.Height - BarH - AttrH - StatusH)); } }
        int Offset { get { return backdrop >= 0 ? Pad : 0; } }

        bool composeDirty = true, sizing;
        Bitmap viewCache; float cacheZoom; bool cacheHq, cacheStale = true;
        bool Interacting { get { return dragging || panning || sizing; } }

        // Composed lazily in paint, at most once per frame.
        void Recompose() { composeDirty = true; cacheStale = true; if (composed == null) EnsureComposed(); Invalidate(); }

        void EnsureComposed()
        {
            if (!composeDirty) return;
            composeDirty = false;
            if (composed != null && composed.Size != baseImg.Size) { if (display != composed && display != null) display.Dispose(); composed.Dispose(); composed = null; display = null; }
            if (composed == null) composed = new Bitmap(baseImg.Width, baseImg.Height, PixelFormat.Format32bppPArgb);
            Renderer.Compose(baseImg, Objs, composed);
            if (display != null && display != composed) display.Dispose();
            display = backdrop >= 0 ? Renderer.Backdrop(composed, backdrop, Pad) : composed;
            cacheStale = true;
        }

        void DrawView(Graphics g, Rectangle v)
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            if (zoom == 1f) g.DrawImageUnscaled(display, v.X, v.Y);
            else if (zoom > 1f)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(display, v);
                g.PixelOffsetMode = PixelOffsetMode.Default;
            }
            else
            {
                bool hq = !Interacting;
                if (viewCache == null || viewCache.Width != v.Width || viewCache.Height != v.Height || cacheZoom != zoom || cacheStale || (hq && !cacheHq))
                {
                    if (viewCache == null || viewCache.Width != v.Width || viewCache.Height != v.Height)
                    {
                        if (viewCache != null) viewCache.Dispose();
                        viewCache = new Bitmap(Math.Max(1, v.Width), Math.Max(1, v.Height), PixelFormat.Format32bppPArgb);
                    }
                    using (var cg = Graphics.FromImage(viewCache))
                    {
                        cg.CompositingMode = CompositingMode.SourceCopy;
                        // Fast while dragging large images, re-scaled properly on release.
                        cg.InterpolationMode = hq ? InterpolationMode.HighQualityBicubic : display.Width * display.Height > 4000000 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                        cg.PixelOffsetMode = PixelOffsetMode.Half;
                        cg.DrawImage(display, 0, 0, viewCache.Width, viewCache.Height);
                    }
                    cacheZoom = zoom; cacheHq = hq; cacheStale = false;
                }
                g.DrawImageUnscaled(viewCache, v.X, v.Y);
            }
            g.CompositingMode = CompositingMode.SourceOver;
        }

        public void TestFrame(Graphics g) { dragging = true; Recompose(); PaintAll(g); dragging = false; }

        Bitmap IImageSource.CurrentImage() { return Export(); }

        public Bitmap Export()
        {
            using (var c = Renderer.Compose(baseImg, Objs))
                return backdrop >= 0 ? Renderer.Backdrop(c, backdrop, Pad) : Imaging.Crop(c, new Rectangle(0, 0, c.Width, c.Height));
        }

        void UpdateFit()
        {
            if (!fit) return;
            var a = CanvasArea;
            zoom = Math.Min(1f, Math.Min((float)a.Width / display.Width, (float)a.Height / display.Height));
            pan = PointF.Empty;
        }

        RectangleF ViewRect()
        {
            var a = CanvasArea;
            float w = display.Width * zoom, h = display.Height * zoom;
            return new RectangleF((float)Math.Round(a.X + (a.Width - w) / 2 + pan.X), (float)Math.Round(a.Y + (a.Height - h) / 2 + pan.Y), (float)Math.Round(w), (float)Math.Round(h));
        }

        PointF ToImage(Point p)
        {
            var v = ViewRect();
            return new PointF((p.X - v.X) / zoom - Offset, (p.Y - v.Y) / zoom - Offset);
        }

        PointF ToScreen(PointF p)
        {
            var v = ViewRect();
            return new PointF(v.X + (p.X + Offset) * zoom, v.Y + (p.Y + Offset) * zoom);
        }

        protected override void OnPaint(PaintEventArgs e) { PaintAll(e.Graphics); }

        public void PaintAll(Graphics g)
        {
            EnsureComposed(); Layout2(); UpdateFit();
            g.Clear(Bg);
            var a = CanvasArea;
            var state = g.Save();
            g.SetClip(a);
            var v = ViewRect();
            if (checker == null)
                using (var tile = new Bitmap(24, 24))
                {
                    using (var cg = Graphics.FromImage(tile))
                    {
                        cg.Clear(Color.FromArgb(30, 30, 32));
                        using (var cb = new SolidBrush(Color.FromArgb(38, 38, 41))) { cg.FillRectangle(cb, 0, 0, 12, 12); cg.FillRectangle(cb, 12, 12, 12, 12); }
                    }
                    checker = new TextureBrush(tile);
                }
            if (!v.Contains(a)) g.FillRectangle(checker, a);
            DrawView(g, Rectangle.Round(v));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (draft != null) DrawDraft(g);
            if (crop.HasValue) DrawCrop(g);
            if (sel != null) DrawHandles(g, sel);
            if (tool == Tool.Ruler && hoverValid) DrawRuler(g);
            g.Restore(state);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            PaintBars(g);
        }

        void DrawDraft(Graphics g)
        {
            var st = g.Save();
            var v = ViewRect();
            g.TranslateTransform(v.X + Offset * zoom, v.Y + Offset * zoom);
            g.ScaleTransform(zoom, zoom);
            if (draft.Kind == Kind.Pixelate || draft.Kind == Kind.Blur || draft.Kind == Kind.Erase || draft.Kind == Kind.Spotlight || draft.Kind == Kind.Magnifier)
                using (var p = new Pen(Color.White, 1.5f / zoom) { DashStyle = DashStyle.Dash }) { var r = draft.Box; g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
            else Renderer.Draw(g, baseImg, baseImg, draft);
            g.Restore(st);
        }

        void DrawCrop(Graphics g)
        {
            var c = crop.Value;
            var p1 = ToScreen(new PointF(c.Left, c.Top)); var p2 = ToScreen(new PointF(c.Right, c.Bottom));
            var r = RectangleF.FromLTRB(p1.X, p1.Y, p2.X, p2.Y);
            using (var rgn = new Region(ViewRect())) using (var b = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
            {
                rgn.Exclude(r); g.FillRegion(b, rgn);
            }
            using (var p = new Pen(Color.White, 1.5f)) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            Chip(g, c.Width + " × " + c.Height + L.T("   ↵ anwenden · A trim · C Farbe"), r.X, r.Y - 26);
        }

        static void Chip(Graphics g, string s, float x, float y)
        {
            using (var f = new Font("Segoe UI", 9f))
            {
                var sz = g.MeasureString(s, f);
                using (var b = new SolidBrush(Color.FromArgb(225, 20, 20, 22))) g.FillRectangle(b, x, y, sz.Width + 8, 22);
                g.DrawString(s, f, Brushes.White, x + 4, y + 3);
            }
        }

        List<PointF> Handles(Obj o)
        {
            if (o.IsLine)
            {
                var c = o.Ctrl ?? new PointF((o.A.X + o.B.X) / 2, (o.A.Y + o.B.Y) / 2);
                return new List<PointF> { o.A, o.B, c };
            }
            if (o.Kind == Kind.Pen || o.Kind == Kind.Highlight || o.Kind == Kind.Text || o.Kind == Kind.Step) return new List<PointF>();
            var r = o.Box;
            return new List<PointF> { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
        }

        void DrawHandles(Graphics g, Obj o)
        {
            var hs = Handles(o);
            if (hs.Count == 0)
            {
                var b = Bounds2(o);
                var p1 = ToScreen(b.Location); var p2 = ToScreen(new PointF(b.Right, b.Bottom));
                using (var p = new Pen(Color.White) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, p1.X - 3, p1.Y - 3, p2.X - p1.X + 6, p2.Y - p1.Y + 6);
            }
            foreach (var h in hs)
            {
                var s = ToScreen(h);
                g.FillEllipse(Brushes.White, s.X - 5, s.Y - 5, 10, 10);
                using (var p = new Pen(Accent, 2)) g.DrawEllipse(p, s.X - 5, s.Y - 5, 10, 10);
            }
        }

        static RectangleF Bounds2(Obj o)
        {
            if (o.Pts != null && o.Pts.Count > 0)
                return RectangleF.FromLTRB(o.Pts.Min(p => p.X), o.Pts.Min(p => p.Y), o.Pts.Max(p => p.X), o.Pts.Max(p => p.Y));
            if (o.Kind == Kind.Step) { float r = 12 + o.Width * 1.6f; return new RectangleF(o.A.X - r, o.A.Y - r, r * 2, r * 2); }
            return o.Box;
        }

        Tuple<PointF, PointF> Span(Point p, bool horizontal)
        {
            var bmp = composed;
            var start = bmp.GetPixel(p.X, p.Y);
            int lo = horizontal ? p.X : p.Y, hi = lo, max = (horizontal ? bmp.Width : bmp.Height) - 1;
            Func<int, Color> at = i => horizontal ? bmp.GetPixel(i, p.Y) : bmp.GetPixel(p.X, i);
            while (lo > 0 && Imaging.ColorDiff(at(lo - 1), start) < 30) lo--;
            while (hi < max && Imaging.ColorDiff(at(hi + 1), start) < 30) hi++;
            hi++;
            return horizontal ? Tuple.Create(new PointF(lo, p.Y), new PointF(hi, p.Y)) : Tuple.Create(new PointF(p.X, lo), new PointF(p.X, hi));
        }

        void DrawRuler(Graphics g)
        {
            if (!new Rectangle(0, 0, composed.Width, composed.Height).Contains(hoverPx)) return;
            Action<bool> one = h =>
            {
                var s = Span(hoverPx, h);
                var a = ToScreen(s.Item1); var b = ToScreen(s.Item2);
                using (var p = new Pen(Accent, 1.5f)) g.DrawLine(p, a, b);
                int len = (int)Math.Round(Math.Max(s.Item2.X - s.Item1.X, s.Item2.Y - s.Item1.Y));
                Chip(g, len + " px", (a.X + b.X) / 2 + 8, (a.Y + b.Y) / 2 + (h ? 8 : -11));
            };
            if (rulerAxis != 2) one(true);
            if (rulerAxis != 1) one(false);
        }

        // Results show up on the button and in a chip, not in a toast at the bottom.
        void Effect(string id, ButtonFx.State state, string message = null)
        {
            fx[id] = new ButtonFx(state);
            if (message != null) Note(message, state == ButtonFx.State.Fail ? 4000 : 2500);
            StartFxTimer();
        }

        void Note(string text, int ms)
        {
            note = text; noteUntil = DateTime.Now.AddMilliseconds(ms);
            StartFxTimer();
        }

        void StartFxTimer()
        {
            if (fxTimer == null)
            {
                fxTimer = new Timer { Interval = 16 };
                fxTimer.Tick += delegate
                {
                    foreach (var id in fx.Where(p => p.Value.Finished).Select(p => p.Key).ToList()) fx.Remove(id);
                    if (note != null && DateTime.Now > noteUntil) note = null;
                    if (fx.Count == 0 && note == null) fxTimer.Stop();
                    Invalidate(new Rectangle(0, 0, ClientSize.Width, BarH + 120));
                };
            }
            fxTimer.Start();
        }

        void DrawEffects(Graphics g)
        {
            foreach (var p in fx)
            {
                var b = buttons.FirstOrDefault(x => x.Id == p.Key);
                if (b == null || b.R.IsEmpty || (b.R.Y >= BarH && !moreOpen)) continue;
                p.Value.Draw(g, b.R.X + b.R.Width / 2f, b.R.Y + b.R.Height / 2f);
            }
            if (note != null) Glint.Chip.Draw(g, note, new Rectangle(ClientSize.Width / 2 - 320, BarH + 10, 640, 28));
        }

        bool Busy(string id) { ButtonFx f; return fx.TryGetValue(id, out f) && f.Kind == ButtonFx.State.Busy; }

        async void RunOcr()
        {
            if (Busy("ocr")) return;
            Effect("ocr", ButtonFx.State.Busy);
            try
            {
                string text;
                using (var x = Export()) text = await Tray.OcrCopy(x);
                if (IsDisposed) return;
                if (text == "") Effect("ocr", ButtonFx.State.Fail, L.T("Kein Text gefunden"));
                else Effect("ocr", ButtonFx.State.Done);
            }
            catch (Exception ex)
            {
                Log.Error("OCR", ex);
                if (!IsDisposed) Effect("ocr", ButtonFx.State.Fail, L.T("Texterkennung fehlgeschlagen"));
            }
        }

        async void RunUpload(Destination d)
        {
            if (Busy("upload")) return;
            if (d == null) { Effect("upload", ButtonFx.State.Fail, L.T("Kein Upload-Ziel eingerichtet")); return; }
            Effect("upload", ButtonFx.State.Busy);
            try
            {
                bool copied;
                using (var x = Export()) copied = await Uploader.Upload(x, d);
                if (!IsDisposed) Effect("upload", ButtonFx.State.Done, copied ? null : L.T("Hochgeladen, Link steht im Log"));
            }
            catch (Exception ex)
            {
                Log.Error("Upload nach " + d.Name + " (" + d.TypeName + ")", ex);
                if (!IsDisposed) Effect("upload", ButtonFx.State.Fail, L.T("Upload fehlgeschlagen"));
            }
        }

        void DrawButton(Graphics g, Btn b)
        {
            ButtonFx running;
            if (fx.TryGetValue(b.Id, out running) && running.HidesIcon) return;
            float cx = b.R.X + b.R.Width / 2f, cy = b.R.Y + b.R.Height / 2f;
            var c = Color.FromArgb(232, 232, 235);
            switch (b.Id)
            {
                case "drag":
                    var grip = Color.FromArgb(125, 128, 134);
                    Icons.DrawLucide(g, "grip-vertical", cx - 17, cy, 18, 1.6f, grip);
                    Icons.DrawLucide(g, "file", cx, cy, 20, 1.5f, c);
                    Icons.DrawLucide(g, "grip-vertical", cx + 17, cy, 18, 1.6f, grip);
                    break;
                case "select": Icons.FillLucide(g, "mouse-pointer-2", cx, cy, 19, 1.4f, c); break;
                case "text":
                    using (var f = new Font("Georgia", 18f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var br = new SolidBrush(c)) g.DrawString("T", f, br, cx, cy, sf);
                    break;
                case "ruler":
                    using (var f = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var br = new SolidBrush(c)) g.DrawString("12", f, br, cx, cy - 5, sf);
                    using (var p = new Pen(c, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(p, cx - 9, cy + 5, cx + 9, cy + 5);
                        g.DrawLine(p, cx - 9, cy + 2, cx - 9, cy + 8);
                        g.DrawLine(p, cx + 9, cy + 2, cx + 9, cy + 8);
                    }
                    break;
                default: Icons.DrawLucide(g, Icons.Name(b.Id), cx, cy, 20, 1.5f, c); break;
            }
        }

        static void InfoBlock(Graphics g, Rectangle r, string top, string bottom)
        {
            using (var f = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
            using (var d = new SolidBrush(Color.FromArgb(150, 150, 156)))
            using (var t = new SolidBrush(Color.FromArgb(235, 235, 238)))
            {
                g.DrawString(top, f, t, r.X, r.Y + 2);
                g.DrawString(bottom, f, d, r.X, r.Y + 18);
            }
        }

        void PaintBars(Graphics g)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var b = new SolidBrush(Bar)) g.FillRectangle(b, 0, 0, ClientSize.Width, BarH);
            using (var p = new Pen(Color.FromArgb(16, 16, 18))) g.DrawLine(p, 0, BarH - 1, ClientSize.Width, BarH - 1);
            using (var p = new Pen(Color.FromArgb(70, 70, 75)))
            {
                foreach (int x in separators) g.DrawLine(p, x, 17, x, 35);
                g.DrawLine(p, sizeRect.X - 8, 13, sizeRect.X - 8, 39);
                g.DrawLine(p, zoomRect.X - 8, 13, zoomRect.X - 8, 39);
            }
            Brand.DrawMark(g, new Rectangle(15, 18, 16, 16), Color.FromArgb(232, 232, 235), Brand.Pixel);
            foreach (var btn in buttons)
            {
                if (btn.R.IsEmpty || btn.R.Y >= BarH) continue;
                bool on = btn.Tool == tool || (btn.Id == "backdrop" && backdrop >= 0) || (btn.Id == "more" && moreOpen);
                if (on || btn == hover)
                    using (var path = Renderer.Round(btn.R, 12)) using (var br = new SolidBrush(on ? Hover : Color.FromArgb(52, 52, 56))) g.FillPath(br, path);
                DrawButton(g, btn);
            }

            bool inside = hoverValid && new Rectangle(0, 0, composed.Width, composed.Height).Contains(hoverPx);
            Color shown = inside ? composed.GetPixel(hoverPx.X, hoverPx.Y) : picked ?? Color.FromArgb(70, 70, 75);
            using (var br = new SolidBrush(shown)) g.FillEllipse(br, colorRect.X, colorRect.Y + 8, 20, 20);
            using (var p = new Pen(Color.FromArgb(50, 255, 255, 255))) g.DrawEllipse(p, colorRect.X, colorRect.Y + 8, 20, 20);
            InfoBlock(g, new Rectangle(colorRect.X + 28, colorRect.Y, 80, 36), Colors.Hex(shown), L.T("Tab: kopieren"));
            InfoBlock(g, sizeRect, display.Width + "×" + display.Height + " px", L.T("Bildgröße"));
            InfoBlock(g, zoomRect, (int)Math.Round(zoom * 100) + " %", "Zoom");
            PaintWindowButtons(g);

            if (!morePanel.IsEmpty)
            {
                using (var path = Renderer.Round(morePanel, 20)) using (var br = new SolidBrush(Color.FromArgb(40, 40, 43))) g.FillPath(br, path);
                using (var path = Renderer.Round(morePanel, 20)) using (var p = new Pen(Color.FromArgb(70, 70, 75))) g.DrawPath(p, path);
                foreach (var btn in buttons)
                {
                    if (btn.R.IsEmpty || btn.R.Y < BarH) continue;
                    bool on = btn.Tool == tool || (btn.Id == "backdrop" && backdrop >= 0);
                    if (on || btn == hover)
                        using (var path = Renderer.Round(btn.R, 12)) using (var br = new SolidBrush(on ? Hover : Color.FromArgb(58, 58, 62))) g.FillPath(br, path);
                    DrawButton(g, btn);
                }
            }

            if (!attrPanel.IsEmpty)
            {
                using (var path = Renderer.Round(attrPanel, 20)) using (var br = new SolidBrush(Color.FromArgb(240, 36, 36, 38))) g.FillPath(br, path);
                using (var path = Renderer.Round(attrPanel, 20)) using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(p, path);
                using (var f = new Font("Segoe UI", 9f))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    foreach (var btn in attrButtons)
                    {
                        if (btn.Id[0] == 'c')
                        {
                            Color c = btn.Id == "cc" ? color : Palette[int.Parse(btn.Id.Substring(1))];
                            using (var br = new SolidBrush(c)) g.FillEllipse(br, btn.R);
                            bool cur = btn.Id != "cc" && c.ToArgb() == color.ToArgb();
                            using (var p = new Pen(cur ? Color.White : Color.FromArgb(60, 255, 255, 255), cur ? 2 : 1)) g.DrawEllipse(p, btn.R);
                            if (btn.Id == "cc") g.DrawString("+", f, Renderer.Contrast(c) == Color.White ? Brushes.White : Brushes.Black, btn.R, sf);
                            continue;
                        }
                        bool active = (btn.Id == "fill" && fill) || (btn.Id == "hand" && hand);
                        using (var path = Renderer.Round(btn.R, 10)) using (var br = new SolidBrush(active ? Hover : btn == hover ? Color.FromArgb(58, 58, 62) : Color.FromArgb(46, 46, 49))) g.FillPath(br, path);
                        string t = btn.Id == "w-" || btn.Id == "o-" ? "−" : btn.Id == "w+" || btn.Id == "o+" ? "+" : btn.Id == "fill" ? L.T("Füllung") : L.T("Skizze");
                        g.DrawString(t, f, Brushes.White, btn.R, sf);
                        if (btn.Id == "w+") g.DrawString(width.ToString("0") + " px", f, Brushes.Silver, btn.R.Right + 4, btn.R.Y + 4);
                        if (btn.Id == "o+") g.DrawString(opacity + " %", f, Brushes.Silver, btn.R.Right + 4, btn.R.Y + 4);
                    }
            }

            if (hover != null && hover.Tip != null && hover.Id[0] != 'c')
            {
                float top = hover.R.Bottom + 8;
                var area = new Rectangle(hover.R.X + hover.R.Width / 2 - 100, (int)top, 200, 28);
                if (area.X < 4) area.X = 4;
                var parts = hover.Tip.Split(new[] { "  " }, StringSplitOptions.None);
                Glint.Chip.Draw(g, parts[0], area, parts.Length > 1 ? parts[1] : null);
            }
            DrawEffects(g);
        }

        void PushUndo()
        {
            undo.Push(Tuple.Create(baseImg, Objs.Select(o => o.Clone()).ToList()));
            redo.Clear();
        }
        void Restore(Stack<Tuple<Bitmap, List<Obj>>> from, Stack<Tuple<Bitmap, List<Obj>>> to)
        {
            if (from.Count == 0) return;
            to.Push(Tuple.Create(baseImg, Objs));
            var s = from.Pop(); baseImg = s.Item1; Objs = s.Item2; sel = null; crop = null;
            Recompose();
        }

        static float Dist(PointF a, PointF b) { return (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        static float SegDist(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
            float t = l == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l));
            return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        Obj HitObject(PointF p)
        {
            float tol = 7 / zoom;
            for (int i = Objs.Count - 1; i >= 0; i--)
            {
                var o = Objs[i];
                if (o.IsLine || o.Pts != null)
                {
                    var pts = o.Pts ?? Renderer.Curve(o).ToList();
                    for (int k = 1; k < pts.Count; k++) if (SegDist(p, pts[k - 1], pts[k]) < tol + o.Width / 2) return o;
                }
                else
                {
                    var b = Bounds2(o); b.Inflate(tol, tol);
                    if (b.Contains(p)) return o;
                }
            }
            return null;
        }

        Btn HitBtn(Point p)
        {
            foreach (var b in buttons) if (b.R.Contains(p)) return b;
            foreach (var b in attrButtons) if (b.R.Contains(p)) return b;
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            CommitText();
            Focus();
            if (e.Button == MouseButtons.Left && closeR.Contains(e.Location)) { Close(); return; }
            if (e.Button == MouseButtons.Left && minR.Contains(e.Location)) { WindowState = FormWindowState.Minimized; return; }
            var hit = HitBtn(e.Location);
            if (hit != null && hit.Id == "upload" && e.Button == MouseButtons.Right) { Uploader.ShowMenu(RunUpload); return; }
            if (hit != null && e.Button == MouseButtons.Left)
            {
                if (hit.Id == "drag") { DragOut(); return; }
                ClickBtn(hit); return;
            }
            if (moreOpen) { moreOpen = false; Invalidate(); }
            if (sizeRect.Contains(e.Location)) { ResizeDialog(); return; }
            if (zoomRect.Contains(e.Location))
            {
                fit = !fit;
                if (!fit) { zoom = 1; pan = PointF.Empty; }
                Invalidate();
                return;
            }
            if (attrPanel.Contains(e.Location)) return;
            if (!CanvasArea.Contains(e.Location)) return;
            if (e.Button == MouseButtons.Middle) { panning = true; panStart = e.Location; fit = false; return; }
            if (e.Button != MouseButtons.Left) return;
            var p = ToImage(e.Location);
            last = downAt = p; dragging = true;
            switch (tool)
            {
                case Tool.Select: BeginSelect(p); break;
                case Tool.Crop: crop = new Rectangle((int)p.X, (int)p.Y, 0, 0); break;
                case Tool.Text: dragging = false; StartText(p, null); break;
                case Tool.Ruler: dragging = false; ImprintRuler(); break;
                case Tool.Step:
                    PushUndo(); dragging = false;
                    Objs.Add(sel = NewObj(Kind.Step, p));
                    sel.Num = Objs.Where(o => o.Kind == Kind.Step).Select(o => o.Num).DefaultIfEmpty(0).Max() + 1;
                    Recompose(); break;
                default:
                    sel = null;
                    draft = NewObj((Kind)Enum.Parse(typeof(Kind), tool == Tool.Pen ? "Pen" : tool.ToString()), p);
                    if (tool == Tool.Pen || tool == Tool.Highlight) draft.Pts = new List<PointF> { p };
                    break;
            }
            Invalidate();
        }

        Obj NewObj(Kind k, PointF p)
        {
            return new Obj { Kind = k, A = p, B = p, Color = color, Width = width, Fill = fill, Hand = hand, Opacity = opacity, Seed = Environment.TickCount & 0xFFFF };
        }

        void BeginSelect(PointF p)
        {
            handle = -1;
            if (sel != null)
            {
                var hs = Handles(sel);
                for (int i = 0; i < hs.Count; i++) if (Dist(hs[i], p) < 9 / zoom) { handle = i; PushUndo(); return; }
            }
            var o = HitObject(p);
            sel = o;
            if (o == null) { dragging = false; return; }
            PushUndo();
            if ((ModifierKeys & Keys.Alt) != 0) { var c = o.Clone(); Objs.Add(c); sel = c; Recompose(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hb = HitBtn(e.Location);
            if (hb != hover) { hover = hb; Invalidate(new Rectangle(0, 0, ClientSize.Width, BarH + 120)); }
            SetWinHover(closeR.Contains(e.Location) ? 3 : minR.Contains(e.Location) ? 1 : 0);
            if (panning) { pan = new PointF(pan.X + e.X - panStart.X, pan.Y + e.Y - panStart.Y); panStart = e.Location; Invalidate(); return; }
            var p = ToImage(e.Location);
            hoverValid = CanvasArea.Contains(e.Location);
            hoverPx = new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
            if (CanvasArea.Contains(e.Location) && !attrPanel.Contains(e.Location))
            {
                if (tool == Tool.Select) Cursor = dragging || HitObject(p) != null ? Cursors.Hand : Cursors.Default;
                else Cursor = tool == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            }
            else Cursor = Cursors.Default;
            if (dragging && e.Button == MouseButtons.Left)
            {
                if (tool == Tool.Select && sel != null) DragSelected(p);
                else if (tool == Tool.Crop)
                {
                    var d = new Rectangle(0, 0, baseImg.Width, baseImg.Height);
                    var r = Rectangle.FromLTRB((int)Math.Min(downAt.X, p.X), (int)Math.Min(downAt.Y, p.Y), (int)Math.Max(downAt.X, p.X), (int)Math.Max(downAt.Y, p.Y));
                    r.Intersect(d); crop = r;
                }
                else if (draft != null)
                {
                    if (draft.Pts != null) draft.Pts.Add(p);
                    else if ((ModifierKeys & Keys.Shift) != 0 && draft.IsLine)
                    {
                        // snap to 45 degrees
                        double ang = Math.Round(Math.Atan2(p.Y - draft.A.Y, p.X - draft.A.X) / (Math.PI / 4)) * (Math.PI / 4);
                        float d = Dist(p, draft.A);
                        draft.B = new PointF(draft.A.X + d * (float)Math.Cos(ang), draft.A.Y + d * (float)Math.Sin(ang));
                    }
                    else draft.B = p;
                }
                last = p;
            }
            if (dragging || tool == Tool.Ruler || panning) Invalidate(); else InvalidateStatus();
        }

        void InvalidateStatus() { Invalidate(colorRect); }

        void DragSelected(PointF p)
        {
            if (handle >= 0)
            {
                if (sel.IsLine)
                {
                    if (handle == 0) sel.A = p; else if (handle == 1) sel.B = p; else sel.Ctrl = p;
                }
                else
                {
                    var r = sel.Box;
                    float l = r.Left, t = r.Top, ri = r.Right, b = r.Bottom;
                    if (handle == 0) { l = p.X; t = p.Y; } else if (handle == 1) { ri = p.X; t = p.Y; }
                    else if (handle == 2) { ri = p.X; b = p.Y; } else { l = p.X; b = p.Y; }
                    sel.A = new PointF(l, t); sel.B = new PointF(ri, b);
                    if (l > ri || t > b) { handle = (handle + 2) % 4; }
                }
            }
            else sel.Move(p.X - last.X, p.Y - last.Y);
            Recompose();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            panning = false;
            if (!dragging) return;
            dragging = false;
            if (draft != null)
            {
                var d = draft; draft = null;
                bool ok = d.Pts != null ? d.Pts.Count > 1 : Dist(d.A, d.B) > 3;
                if (ok)
                {
                    PushUndo(); Objs.Add(d);
                    sel = d.Pts != null ? null : d;
                    if (d.Pts == null) tool = Tool.Select; // pen and highlighter stay active
                    Recompose();
                }
            }
            handle = -1;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (!CanvasArea.Contains(e.Location) || tool != Tool.Select) return;
            var o = HitObject(ToImage(e.Location));
            if (o != null && o.Kind == Kind.Text) StartText(o.A, o);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0) { Zoom(e.Delta > 0 ? 1.15f : 1 / 1.15f); return; }
            fit = false;
            if ((ModifierKeys & Keys.Shift) != 0) pan.X += e.Delta / 2; else pan.Y += e.Delta / 2;
            Invalidate();
        }

        void Zoom(float f) { fit = false; zoom = Math.Max(.05f, Math.Min(16f, zoom * f)); Invalidate(); }

        void StartText(PointF p, Obj existing)
        {
            if (existing != null) { PushUndo(); }
            editingNew = existing == null;
            editing = existing;
            var a = ToScreen(p);
            var proto = existing ?? NewObj(Kind.Text, p);
            if (existing == null) editing = proto;
            var font = Renderer.TextFont(proto);
            editor = new TextBox { Multiline = true, BorderStyle = BorderStyle.FixedSingle, Text = proto.Text, BackColor = Color.FromArgb(40, 40, 44), ForeColor = proto.Color, Font = new Font(font.FontFamily, Math.Max(10, font.Size * zoom), font.Style, GraphicsUnit.Pixel), AcceptsReturn = true };
            font.Dispose();
            editor.SetBounds((int)a.X, (int)a.Y, 320, (int)(editor.Font.Height * 2.4f));
            editor.KeyDown += (s, ev) =>
            {
                if (ev.KeyCode == Keys.Escape || (ev.KeyCode == Keys.Enter && ev.Control)) { ev.SuppressKeyPress = true; CommitText(); }
            };
            Controls.Add(editor); editor.Focus();
            if (existing != null) { Objs.Remove(existing); Recompose(); }
        }

        void CommitText()
        {
            if (editor == null) return;
            var ed = editor; var o = editing; editor = null; editing = null;
            o.Text = ed.Text.TrimEnd();
            Controls.Remove(ed); ed.Dispose();
            if (editingNew && o.Text.Length > 0) PushUndo(); // existing text was pushed in StartText
            if (o.Text.Length > 0) { Objs.Add(o); sel = o; tool = Tool.Select; }
            Recompose(); Focus();
        }

        void ImprintRuler()
        {
            if (!new Rectangle(0, 0, composed.Width, composed.Height).Contains(hoverPx)) return;
            PushUndo();
            Action<bool> add = h =>
            {
                var s = Span(hoverPx, h);
                Objs.Add(new Obj { Kind = Kind.Measure, A = s.Item1, B = s.Item2, Color = color });
            };
            if (rulerAxis != 2) add(true);
            if (rulerAxis != 1) add(false);
            Recompose();
        }

        void ClickBtn(Btn b)
        {
            if (b.Id == "more") { moreOpen = !moreOpen; Invalidate(); return; }
            moreOpen = false;
            if (b.Tool.HasValue) { SetTool(b.Tool.Value); return; }
            switch (b.Id)
            {
                case "undo": Restore(undo, redo); break;
                case "redo": Restore(redo, undo); break;
                case "backdrop": backdrop = backdrop + 1 >= Renderer.Gradients.Length ? -1 : backdrop + 1; Recompose(); break;
                case "ocr": RunOcr(); break;
                case "addcap": AddCapture(); break;
                case "pin": new PinWindow(Export()).Show(); break;
                case "upload": RunUpload(Uploader.Active); break;
                case "save": Save(); break;
                case "copy": CopyImage(); break;
                case "cc":
                    using (var d = new ColorPicker(color)) if (d.ShowDialog(this) == DialogResult.OK) SetColor(d.Value);
                    break;
                case "w-": SetWidth(Math.Max(1, width - 1)); break;
                case "w+": SetWidth(Math.Min(40, width + 1)); break;
                case "o-": SetOpacity(Math.Max(10, opacity - 10)); break;
                case "o+": SetOpacity(Math.Min(100, opacity + 10)); break;
                case "fill": fill = !fill; Apply(o => o.Fill = fill); break;
                case "hand": hand = !hand; Apply(o => o.Hand = hand); break;
                default:
                    if (b.Id[0] == 'c') SetColor(Palette[int.Parse(b.Id.Substring(1))]);
                    break;
            }
            Invalidate();
        }

        void SetColor(Color c) { color = c; Apply(o => o.Color = c); }
        void SetWidth(float w) { width = w; Apply(o => o.Width = w); }
        void SetOpacity(int v) { opacity = v; Apply(o => o.Opacity = v); }
        void Apply(Action<Obj> a)
        {
            if (sel == null) { Invalidate(); return; }
            PushUndo(); a(sel); Recompose();
        }

        void SetTool(Tool t)
        {
            CommitText();
            tool = t; crop = null; draft = null; rulerAxis = 0;
            if (t != Tool.Select) sel = null;
            Invalidate();
        }

        void CopyImage()
        {
            try
            {
                using (var b = Export()) Imaging.Copy(b);
                Effect("copy", ButtonFx.State.Done);
            }
            catch (Exception ex)
            {
                Log.Error("Kopieren", ex);
                Effect("copy", ButtonFx.State.Fail, L.T("Kopieren fehlgeschlagen"));
            }
        }

        void Save()
        {
            try
            {
                using (var b = Export())
                {
                    var f = Imaging.QuickSave(b);
                    Effect("save", ButtonFx.State.Done, L.T("Gespeichert: ") + Path.GetFileName(f));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Speichern", ex);
                Effect("save", ButtonFx.State.Fail, L.T("Speichern fehlgeschlagen"));
            }
        }

        void SaveAs()
        {
            using (var d = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|WebP|*.webp", FileName = FileNames.ForSave(baseImg.Width, baseImg.Height), InitialDirectory = Settings.Current.SaveDir })
                if (d.ShowDialog() == DialogResult.OK)
                    using (var b = Export()) Imaging.SaveFile(b, d.FileName);
        }

        void DragOut()
        {
            var path = Imaging.TempFile(".png");
            using (var b = Export()) Imaging.SaveFile(b, path);
            DoDragDrop(new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        }

        void AddCapture()
        {
            Hide();
            var t = new Timer { Interval = 180 };
            t.Tick += delegate
            {
                t.Dispose();
                var o = new Overlay(false);
                Overlay.Active = true;
                o.Done = r => AddImage(r.Item2);
                o.FormClosed += delegate { Show(); Activate(); };
                o.Show();
            };
            t.Start();
        }

        public void AddImage(Bitmap img)
        {
            PushUndo();
            float x = Math.Max(0, (baseImg.Width - img.Width) / 2f), y = Math.Max(0, (baseImg.Height - img.Height) / 2f);
            sel = new Obj { Kind = Kind.Image, Img = img, A = new PointF(x, y), B = new PointF(x + img.Width, y + img.Height) };
            Objs.Add(sel);
            tool = Tool.Select; Recompose();
        }

        void ResizeDialog()
        {
            using (var f = new Form { Text = L.T("Bildgröße"), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(260, 90), BackColor = Bar, ForeColor = Color.White })
            {
                f.Controls.Add(new Label { Text = L.T("Breite in Pixeln"), Left = 12, Top = 15, Width = 110 });
                var n = new NumericUpDown { Left = 130, Top = 12, Width = 110, Minimum = 16, Maximum = 20000, Value = Math.Min(20000, Math.Max(16, baseImg.Width)) };
                var ok = new Button { Text = "OK", Left = 165, Top = 50, Width = 75, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
                f.Controls.Add(n); f.Controls.Add(ok); f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                float s = (float)n.Value / baseImg.Width;
                int h = Math.Max(1, (int)(baseImg.Height * s));
                PushUndo();
                using (var flat = Renderer.Compose(baseImg, Objs))
                {
                    var res = new Bitmap((int)n.Value, h, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(res)) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.Half; g.DrawImage(flat, 0, 0, res.Width, res.Height); }
                    baseImg = res; Objs = new List<Obj>(); sel = null; fit = true; Recompose();
                }
            }
        }

        protected override void Dispose(bool d)
        {
            if (d)
            {
                var owned = new HashSet<Bitmap> { baseImg, composed, display, viewCache };
                foreach (var state in undo.Concat(redo))
                {
                    owned.Add(state.Item1);
                    foreach (var o in state.Item2) owned.Add(o.Img);
                }
                foreach (var o in Objs) owned.Add(o.Img);
                foreach (var b in owned) if (b != null) b.Dispose();
                undo.Clear(); redo.Clear();
                if (fxTimer != null) fxTimer.Dispose();
            }
            base.Dispose(d);
        }

        void ApplyCrop()
        {
            if (!crop.HasValue || crop.Value.Width < 2 || crop.Value.Height < 2) { crop = null; return; }
            PushUndo();
            var c = crop.Value;
            baseImg = Imaging.Crop(baseImg, c);
            foreach (var o in Objs) o.Move(-c.X, -c.Y);
            crop = null; tool = Tool.Select; fit = true; Recompose();
        }

        void CopyColor(Color c)
        {
            prevPicked = picked; picked = c;
            string t = Colors.Format(c, Settings.Current.ColorFormat);
            Clipboard.SetText(t);
            statusNote = L.T("Kopiert: ") + t;
            if (prevPicked.HasValue)
                statusNote += string.Format(L.T("   ·   Kontrast zu {0}: {1:0.00}:1 (WCAG)   Lc {2:0} (APCA)"), Colors.Hex(prevPicked.Value), Colors.Wcag(c, prevPicked.Value), Colors.Apca(c, prevPicked.Value));
            Note(statusNote, prevPicked.HasValue ? 3500 : 1600);
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (editor != null) return base.ProcessCmdKey(ref m, k);
            var code = k & Keys.KeyCode;
            bool ctrl = (k & Keys.Control) != 0, shift = (k & Keys.Shift) != 0;
            if (code == Keys.Tab && hoverValid && new Rectangle(0, 0, composed.Width, composed.Height).Contains(hoverPx))
            {
                CopyColor(shift ? Imaging.Darkest(composed, hoverPx) : composed.GetPixel(hoverPx.X, hoverPx.Y));
                return true;
            }
            if (code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down)
            {
                if (tool == Tool.Ruler) { rulerAxis = code == Keys.Left || code == Keys.Right ? 1 : 2; Invalidate(); return true; }
                if (sel != null)
                {
                    float s = shift ? 10 : 1;
                    PushUndo();
                    sel.Move(code == Keys.Left ? -s : code == Keys.Right ? s : 0, code == Keys.Up ? -s : code == Keys.Down ? s : 0);
                    Recompose(); return true;
                }
            }
            if (ctrl)
            {
                switch (code)
                {
                    case Keys.C: CopyImage(); return true;
                    case Keys.S: if (shift) SaveAs(); else Save(); return true;
                    case Keys.Z: if (shift) Restore(redo, undo); else Restore(undo, redo); return true;
                    case Keys.Y: Restore(redo, undo); return true;
                    case Keys.P: new PinWindow(Export()).Show(); return true;
                    case Keys.U: RunUpload(Uploader.Active); return true;
                    case Keys.T: RunOcr(); return true;
                    case Keys.O: Tray.Instance.OpenFile(); return true;
                    case Keys.Oemplus: case Keys.Add: Zoom(1.25f); return true;
                    case Keys.OemMinus: case Keys.Subtract: Zoom(.8f); return true;
                    case Keys.D0: case Keys.NumPad0: fit = true; Invalidate(); return true;
                    case Keys.D1: case Keys.NumPad1: fit = false; zoom = 1; pan = PointF.Empty; Invalidate(); return true;
                    case Keys.V:
                        if (Clipboard.ContainsImage()) using (var i = Clipboard.GetImage()) AddImage(Imaging.Normalize(i));
                        return true;
                }
                return base.ProcessCmdKey(ref m, k);
            }
            switch (code)
            {
                case Keys.Escape:
                    if (crop.HasValue) crop = null;
                    else if (sel != null) sel = null;
                    else if (tool != Tool.Select) SetTool(Tool.Select);
                    else Close();
                    Invalidate(); return true;
                case Keys.Enter: if (crop.HasValue) { ApplyCrop(); return true; } break;
                case Keys.Delete:
                case Keys.Back:
                    if (sel != null) { PushUndo(); Objs.Remove(sel); sel = null; Recompose(); }
                    return true;
                case Keys.F:
                    var s = Settings.Current;
                    s.ColorFormat = s.ColorFormat == "hex" ? "rgb" : s.ColorFormat == "rgb" ? "oklch" : "hex";
                    s.Save(); Note(L.T("Farbformat: ") + s.ColorFormat.ToUpper(), 1600); return true;
            }
            if (tool == Tool.Crop && crop.HasValue)
            {
                if (code == Keys.A) { crop = Imaging.Trim(composed, crop.Value); Invalidate(); return true; }
                if (code == Keys.C) { CopyColor(Imaging.Average(composed, crop.Value)); return true; }
            }
            var map = new Dictionary<Keys, Tool> { {Keys.V,Tool.Select},{Keys.X,Tool.Crop},{Keys.A,Tool.Arrow},{Keys.L,Tool.Line},{Keys.R,Tool.Rect},{Keys.O,Tool.Oval},{Keys.T,Tool.Text},{Keys.N,Tool.Step},{Keys.P,Tool.Pen},{Keys.H,Tool.Highlight},{Keys.S,Tool.Spotlight},{Keys.M,Tool.Magnifier},{Keys.K,Tool.Pixelate},{Keys.B,Tool.Blur},{Keys.E,Tool.Erase},{Keys.U,Tool.Ruler} };
            Tool mapped;
            if (map.TryGetValue(code, out mapped)) { SetTool(mapped); return true; }
            return base.ProcessCmdKey(ref m, k);
        }

        protected override void OnMouseLeave(EventArgs e) { SetWinHover(0); base.OnMouseLeave(e); }

        protected override void OnDragEnter(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                try { AddImage(Imaging.Load(f)); }
                catch (Exception ex) { Log.Error("Einfügen von " + f, ex); Note(L.T("Keine gültige Bilddatei"), 4000); }
        }

        public Tool CurrentTool { get { return tool; } }
        public Obj Selected { get { return sel; } }
        public void TestDrag(Tool t, PointF from, PointF to)
        {
            if (t != tool) SetTool(t);
            var a = Point.Round(ToScreen(from)); var b = Point.Round(ToScreen(to));
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, b.X, b.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, b.X, b.Y, 0));
        }
        public void SetBackdrop(int i) { backdrop = i; Recompose(); }
        public void SetFitSize(Size s) { ClientSize = s; }
    }
}
