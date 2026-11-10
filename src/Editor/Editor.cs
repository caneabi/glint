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
    public partial class Editor : Form, IImageSource
    {
        const int BarH = 52, AttrH = 0, StatusH = 0;
        static readonly Color Bg = Ui.Bg, Bar = Ui.Side, Hover = Ui.Well, Accent = Ui.Accent;
        static readonly Color[] Palette = { Color.FromArgb(255, 94, 91), Color.FromArgb(255, 170, 40), Color.FromArgb(255, 221, 50), Color.FromArgb(70, 200, 110), Color.FromArgb(60, 140, 255), Color.FromArgb(170, 100, 245), Color.White, Color.Black };

        readonly EditorDocument doc;
        Bitmap composed, display;
        public List<Obj> Objs { get { return doc.Objs; } }
        Tool tool = Tool.Select;
        Obj sel, draft;
        Color color = Palette[0]; float width = 4; bool fill, hand; int opacity = 100;
        int backdrop = -1; const int Pad = 64; // preset index, -1 off
        Color? backdropColor; // own color instead of a preset
        Color[] BackdropColors { get { return backdrop < 0 ? null : backdropColor.HasValue ? Renderer.Custom(backdropColor.Value) : Renderer.Gradients[backdrop % Renderer.Gradients.Length]; } }
        string textFont = "Segoe UI";
        bool backdropOpen, sliding;
        Bitmap backdropBg; string backdropKey; // cached gradient and shadow
        Rectangle backdropPanel;
        readonly List<Btn> backdropBtns = new List<Btn>();
        int magShape, magZoom = 2;
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
        System.Threading.CancellationTokenSource uploadCancel;
        string note;
        DateTime noteUntil;

        public Editor(Bitmap img)
        {
            doc = new EditorDocument(img);
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

        Rectangle CanvasArea { get { return new Rectangle(0, BarH + AttrH, ClientSize.Width, Math.Max(1, ClientSize.Height - BarH - AttrH - StatusH)); } }
        int Offset { get { return backdrop >= 0 ? Pad : 0; } }

        bool composeDirty = true, sizing;
        Bitmap viewCache; float cacheZoom; bool cacheHq, cacheStale = true;
        bool Interacting { get { return dragging || panning || sizing; } }

        // Composed lazily in paint, at most once per frame.
        void Recompose() { composeDirty = true; partialOnly = false; cacheStale = true; if (composed == null) EnsureComposed(); Invalidate(); }

        // Moving one object only changes where it was and where it is now.
        bool partialOnly; Rectangle partial, viewDirty;
        void RecomposeArea(Rectangle area)
        {
            if (!composeDirty) { partial = area; partialOnly = true; }
            else if (partialOnly) partial = Rectangle.Union(partial, area);
            composeDirty = true;
            InvalidateImage(area);
        }

        public Rectangle LastDirty; // test hook: the window part the last move repainted

        // Repaints only the window part that shows this image area, handles included.
        void InvalidateImage(Rectangle area)
        {
            var p1 = ToScreen(new PointF(area.Left, area.Top)); var p2 = ToScreen(new PointF(area.Right, area.Bottom));
            LastDirty = Rectangle.Inflate(Rectangle.FromLTRB((int)Math.Floor(p1.X), (int)Math.Floor(p1.Y), (int)Math.Ceiling(p2.X), (int)Math.Ceiling(p2.Y)), 14, 14);
            Invalidate(LastDirty);
        }

        static Rectangle AreaOf(Obj o) { return Renderer.Reach(o); }

        void EnsureComposed()
        {
            if (!composeDirty) return;
            composeDirty = false;
            if (composed != null && composed.Size != doc.Base.Size) { if (display != composed && display != null) display.Dispose(); composed.Dispose(); composed = null; display = null; }
            bool fresh = composed == null;
            if (fresh) composed = new Bitmap(doc.Base.Width, doc.Base.Height, PixelFormat.Format32bppPArgb);
            if (partialOnly && !fresh && display != null)
            {
                partialOnly = false;
                var area = Rectangle.Intersect(partial, new Rectangle(0, 0, composed.Width, composed.Height));
                if (area.Width <= 0 || area.Height <= 0) return;
                Renderer.Compose(doc.Base, Objs, composed, area);
                if (backdrop >= 0 && display != composed) Renderer.BackdropCompose(backdropBg, composed, Pad, display, area);
                var shown = area; shown.Offset(Offset, Offset);
                viewDirty = viewDirty.IsEmpty ? shown : Rectangle.Union(viewDirty, shown);
                return;
            }
            partialOnly = false;
            Renderer.Compose(doc.Base, Objs, composed);
            if (backdrop >= 0)
            {
                var cols = BackdropColors;
                string key = composed.Width + "x" + composed.Height + cols[0].ToArgb() + cols[1].ToArgb();
                if (key != backdropKey) { if (backdropBg != null) backdropBg.Dispose(); backdropBg = Renderer.BackdropBase(composed.Width, composed.Height, cols, Pad); backdropKey = key; }
                if (display == null || display == composed || display.Size != backdropBg.Size)
                {
                    if (display != null && display != composed) display.Dispose();
                    display = new Bitmap(backdropBg.Width, backdropBg.Height, PixelFormat.Format32bppArgb);
                }
                Renderer.BackdropCompose(backdropBg, composed, Pad, display);
            }
            else
            {
                if (display != null && display != composed) display.Dispose();
                display = composed;
            }
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
                    cacheZoom = zoom; cacheHq = hq; cacheStale = false; viewDirty = Rectangle.Empty;
                }
                else if (!viewDirty.IsEmpty)
                {
                    // Rescale only the part that changed.
                    float k = viewCache.Width / (float)display.Width;
                    var d = Rectangle.Inflate(Rectangle.FromLTRB((int)(viewDirty.Left * k), (int)(viewDirty.Top * k), (int)Math.Ceiling(viewDirty.Right * k), (int)Math.Ceiling(viewDirty.Bottom * k)), 2, 2);
                    d.Intersect(new Rectangle(0, 0, viewCache.Width, viewCache.Height));
                    using (var cg = Graphics.FromImage(viewCache))
                    {
                        cg.CompositingMode = CompositingMode.SourceCopy;
                        cg.InterpolationMode = InterpolationMode.Bilinear;
                        cg.PixelOffsetMode = PixelOffsetMode.Half;
                        cg.DrawImage(display, d, new RectangleF(d.X / k, d.Y / k, d.Width / k, d.Height / k), GraphicsUnit.Pixel);
                    }
                    cacheHq = false; viewDirty = Rectangle.Empty;
                }
                g.DrawImageUnscaled(viewCache, v.X, v.Y);
            }
            g.CompositingMode = CompositingMode.SourceOver;
        }

        public void TestFrame(Graphics g) { dragging = true; Recompose(); PaintAll(g); dragging = false; }

        Bitmap IImageSource.CurrentImage() { return Export(); }

        public Bitmap Export() { return doc.Export(BackdropColors, Pad); }

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
            g.SetClip(a, CombineMode.Intersect);
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
            // Bars, panels and tips sit at the top, skip them when the repaint is somewhere else.
            var bars = new Rectangle(0, 0, ClientSize.Width, Math.Max(BarH + 130, Math.Max(attrPanel.Bottom, backdropPanel.Bottom) + 50));
            if (!morePanel.IsEmpty) bars = Rectangle.Union(bars, Rectangle.Inflate(morePanel, 4, 40));
            if (g.ClipBounds.IntersectsWith(bars)) PaintBars(g);
        }

        void DrawDraft(Graphics g)
        {
            var st = g.Save();
            var v = ViewRect();
            g.TranslateTransform(v.X + Offset * zoom, v.Y + Offset * zoom);
            g.ScaleTransform(zoom, zoom);
            if (draft.Kind == Kind.Pixelate || draft.Kind == Kind.Blur || draft.Kind == Kind.Erase || draft.Kind == Kind.Spotlight || draft.Kind == Kind.Magnifier)
                using (var p = new Pen(Color.White, 1.5f / zoom) { DashStyle = DashStyle.Dash }) { var r = draft.Box; g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
            else Renderer.Draw(g, doc.Base, doc.Base, draft);
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
            if (o.Kind == Kind.Pen || o.Kind == Kind.Highlight || o.Kind == Kind.Step) return new List<PointF>();
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

        void Undo() { if (doc.Undo()) AfterHistory(); }
        void Redo() { if (doc.Redo()) AfterHistory(); }
        void AfterHistory() { sel = null; crop = null; Recompose(); }

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
            if (moreOpen || backdropOpen) { moreOpen = backdropOpen = false; Invalidate(); }
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
            if (e.Button == MouseButtons.Right) { CanvasMenu(e.Location); return; }
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
                    doc.Push(); dragging = false;
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
            return new Obj { Kind = k, A = p, B = p, Color = color, Width = width, Fill = fill, Hand = hand, Opacity = opacity, Seed = Environment.TickCount & 0xFFFF, Font = textFont, Zoom = magZoom, Shape = magShape };
        }

        void BeginSelect(PointF p)
        {
            handle = -1;
            if (sel != null)
            {
                var hs = Handles(sel);
                for (int i = 0; i < hs.Count; i++) if (Dist(hs[i], p) < 9 / zoom) { handle = i; doc.Push(); return; }
            }
            var o = HitObject(p);
            sel = o;
            if (o == null) { dragging = false; return; }
            doc.Push();
            if ((ModifierKeys & Keys.Alt) != 0) { var c = o.Clone(); Objs.Add(c); sel = c; Recompose(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sliding) { SetStrength(e.X); return; }
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
                    var d = new Rectangle(0, 0, doc.Base.Width, doc.Base.Height);
                    var r = Rectangle.FromLTRB((int)Math.Min(downAt.X, p.X), (int)Math.Min(downAt.Y, p.Y), (int)Math.Max(downAt.X, p.X), (int)Math.Max(downAt.Y, p.Y));
                    r.Intersect(d); crop = r;
                }
                else if (draft != null)
                {
                    if (draft.Pts != null && (ModifierKeys & Keys.Shift) != 0)
                    {
                        // Shift draws a straight stroke, snapped to 45 degrees
                        var a0 = draft.Pts[0];
                        double ang = Math.Round(Math.Atan2(p.Y - a0.Y, p.X - a0.X) / (Math.PI / 4)) * (Math.PI / 4);
                        float len = Dist(p, a0);
                        draft.Pts = new List<PointF> { a0, new PointF(a0.X + len * (float)Math.Cos(ang), a0.Y + len * (float)Math.Sin(ang)) };
                    }
                    else if (draft.Pts != null) draft.Pts.Add(p);
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
            if (dragging && tool == Tool.Select && sel != null && handle < 0) { } // the move already repainted its area
            else if (dragging || tool == Tool.Ruler || panning) Invalidate(); else InvalidateStatus();
        }

        void InvalidateStatus() { Invalidate(colorRect); }

        void DragSelected(PointF p)
        {
            if (handle >= 0 && sel.Kind == Kind.Text) ScaleText(p);
            else if (handle >= 0)
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
            else
            {
                var before = AreaOf(sel);
                sel.Move(p.X - last.X, p.Y - last.Y);
                if (sel.Kind == Kind.Spotlight) Recompose(); // dims the whole image
                else RecomposeArea(Rectangle.Union(before, AreaOf(sel)));
                return;
            }
            Recompose();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            panning = false; sliding = false;
            if (!dragging) return;
            dragging = false;
            if (draft != null)
            {
                var d = draft; draft = null;
                bool ok = d.Pts != null ? d.Pts.Count > 1 : Dist(d.A, d.B) > 3;
                if (ok)
                {
                    doc.Push(); Objs.Add(d);
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
            if (existing != null) { doc.Push(); }
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
            if (editingNew && o.Text.Length > 0) doc.Push(); // existing text was pushed in StartText
            if (o.Text.Length > 0) { Objs.Add(o); sel = o; tool = Tool.Select; }
            Recompose(); Focus();
        }

        void ImprintRuler()
        {
            if (!new Rectangle(0, 0, composed.Width, composed.Height).Contains(hoverPx)) return;
            doc.Push();
            Action<bool> add = h =>
            {
                var s = Span(hoverPx, h);
                Objs.Add(new Obj { Kind = Kind.Measure, A = s.Item1, B = s.Item2, Color = color });
            };
            if (rulerAxis != 2) add(true);
            if (rulerAxis != 1) add(false);
            Recompose();
        }

        void SetTool(Tool t)
        {
            CommitText();
            tool = t; crop = null; draft = null; rulerAxis = 0;
            if (t != Tool.Select) sel = null;
            Invalidate();
        }

        protected override void Dispose(bool d)
        {
            if (d)
            {
                foreach (var b in new HashSet<Bitmap> { composed, display, viewCache, backdropBg }) if (b != null) b.Dispose();
                doc.Dispose();
                if (uploadCancel != null) uploadCancel.Cancel();
                if (fxTimer != null) fxTimer.Dispose();
            }
            base.Dispose(d);
        }

        void ApplyCrop()
        {
            if (!crop.HasValue || crop.Value.Width < 2 || crop.Value.Height < 2) { crop = null; return; }
            doc.Crop(crop.Value);
            crop = null; tool = Tool.Select; fit = true; Recompose();
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
                    doc.Push();
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
                    case Keys.Z: if (shift) Redo(); else Undo(); return true;
                    case Keys.Y: Redo(); return true;
                    case Keys.P: new PinWindow(Export()).Show(); Close(); return true;
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
                    if (sel != null) { doc.Push(); Objs.Remove(sel); sel = null; Recompose(); }
                    return true;
                case Keys.F:
                    var s = Settings.Current;
                    s.ColorFormat = Colors.Formats[(Array.IndexOf(Colors.Formats, s.ColorFormat) + 1) % Colors.Formats.Length];
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
        // Test hook: drags the object under 'from' in small steps and returns ms per step including paint.
        public double TestMoveSelected(PointF from, int steps, Graphics g)
        {
            SetTool(Tool.Select);
            var a = Point.Round(ToScreen(from));
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, a.X, a.Y, 0));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 1; i <= steps; i++) { OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, a.X + i * 3, a.Y + i, 0)); var st = g.Save(); g.SetClip(LastDirty); PaintAll(g); g.Restore(st); }
            double ms = sw.Elapsed.TotalMilliseconds / steps;
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, a.X + steps * 3, a.Y + steps, 0));
            return ms;
        }

        public void TestUi(Tool t, bool backdropChoices) { SetTool(t); backdropOpen = backdropChoices; Invalidate(); }

        public void SetBackdrop(int i) { backdrop = i; backdropColor = null; Recompose(); }
        public void SetFitSize(Size s) { ClientSize = s; }
    }
}
