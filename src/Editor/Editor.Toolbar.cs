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
    // Top bar: buttons, layout, painting and the result effects.
    public partial class Editor
    {
        void SetWinHover(int v) { if (winHover != v) { winHover = v; Invalidate(new Rectangle(minR.X, 0, 138, BarH)); } }

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
            add("erase", L.T("Entfernen  E"), Tool.Erase);
            add("undo", L.T("Rückgängig  Strg+Z"), null); add("redo", L.T("Wiederholen  Strg+Y"), null); add("backdrop", "Backdrop", null); add("addcap", L.T("Aufnahme hinzufügen"), null);
            buttons.Add(new Btn { Id = "more", Tip = L.T("Weitere Werkzeuge") });
        }

        // Kind the attribute bar talks about: the selection, or what the current tool makes.
        Kind? AttrKind
        {
            get
            {
                if (sel != null) return sel.Kind;
                Kind k;
                return Enum.TryParse(tool.ToString(), out k) ? k : (Kind?)null; // tools are named after what they draw
            }
        }

        List<Tuple<string, int, string>> AttrExtras()
        {
            var list = new List<Tuple<string, int, string>>();
            if (AttrKind == Kind.Text) list.Add(Tuple.Create("font", 96, L.T("Schriftart")));
            if (AttrKind == Kind.Magnifier) { list.Add(Tuple.Create("shape", 74, L.T("Form"))); list.Add(Tuple.Create("zoom", 40, "Zoom")); }
            if (AttrKind == Kind.Blur || AttrKind == Kind.Pixelate) { list.Add(Tuple.Create("mode", 112, L.T("Weich oder Pixel"))); list.Add(Tuple.Create("strength", 120, L.T("Stärke"))); }
            return list;
        }

        int EffectStrength { get { return (int)Math.Round(sel != null && (sel.Kind == Kind.Blur || sel.Kind == Kind.Pixelate) ? sel.Width : width); } }

        // Two halves, the active one lit, like the segmented controls in the settings.
        void DrawMode(Graphics g, Rectangle r, Font f, StringFormat sf)
        {
            bool pixel = AttrKind == Kind.Pixelate;
            using (var path = Renderer.Round(r, Ui.ControlRadius)) using (var br = new SolidBrush(Color.FromArgb(46, 46, 49))) g.FillPath(br, path);
            var half = new Rectangle(pixel ? r.X + r.Width / 2 : r.X, r.Y, r.Width / 2, r.Height);
            using (var path = Renderer.Round(Rectangle.Inflate(half, -2, -2), Ui.ControlRadius - 4)) using (var br = new SolidBrush(Hover)) g.FillPath(br, path);
            g.DrawString(L.T("Weich"), f, pixel ? Brushes.Silver : Brushes.White, new Rectangle(r.X, r.Y, r.Width / 2, r.Height), sf);
            g.DrawString("Pixel", f, pixel ? Brushes.White : Brushes.Silver, new Rectangle(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height), sf);
        }

        const int StrengthMax = 20;

        void DrawStrength(Graphics g, Rectangle r, Font f)
        {
            float t = (EffectStrength - 1) / (float)(StrengthMax - 1), track = r.Width - 30, x = r.X + 6 + track * t, cy = r.Y + r.Height / 2f;
            using (var path = Renderer.Round(new RectangleF(r.X + 6, cy - 2, track, 4), 4)) using (var br = new SolidBrush(Color.FromArgb(70, 70, 76))) g.FillPath(br, path);
            using (var path = Renderer.Round(new RectangleF(r.X + 6, cy - 2, Math.Max(4, x - r.X - 6), 4), 4)) using (var br = new SolidBrush(Accent)) g.FillPath(br, path);
            g.FillEllipse(Brushes.White, x - 6, cy - 6, 12, 12);
            g.DrawString(EffectStrength.ToString(), f, Brushes.Silver, r.Right - 20, r.Y + 4);
        }

        void SetStrength(int mouseX)
        {
            var b = attrButtons.FirstOrDefault(x => x.Id == "strength");
            if (b == null) return;
            float t = Math.Max(0, Math.Min(1, (mouseX - b.R.X - 6) / (float)(b.R.Width - 30)));
            int v = 1 + (int)Math.Round(t * (StrengthMax - 1));
            width = v;
            if (sel != null && (sel.Kind == Kind.Blur || sel.Kind == Kind.Pixelate)) { sel.Width = v; Recompose(); }
            Invalidate();
        }

        static readonly string[] ShapeNames = { "Kreis", "Abgerundet", "Quadrat" };

        string AttrLabel(string id)
        {
            if (id == "font") { string f = sel != null && sel.Kind == Kind.Text ? sel.Font : textFont; return f.Length > 13 ? f.Substring(0, 12) + "…" : f; }
            if (id == "shape") return L.T(ShapeNames[sel != null && sel.Kind == Kind.Magnifier ? sel.Shape : magShape]);
            if (id == "zoom") return (sel != null && sel.Kind == Kind.Magnifier ? sel.Zoom : magZoom) + "×";
            return "";
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
                // Only the options that change something for this kind.
                var k = AttrKind;
                bool colorOn = k != Kind.Blur && k != Kind.Pixelate && k != Kind.Erase && k != Kind.Image;
                bool widthOn = k != Kind.Text && k != Kind.Blur && k != Kind.Pixelate && k != Kind.Erase && k != Kind.Image && k != Kind.Spotlight;
                bool fillOn = k == Kind.Rect || k == Kind.Oval || k == Kind.Text;
                bool handOn = k == Kind.Rect || k == Kind.Oval || k == Kind.Arrow || k == Kind.Line || k == Kind.Text;
                bool opacityOn = k != Kind.Blur && k != Kind.Pixelate && k != Kind.Erase;
                var extras = AttrExtras();
                int ax = 13;
                if (colorOn)
                {
                    for (int i = 0; i < Palette.Length; i++) { attrButtons.Add(new Btn { Id = "c" + i, Tip = L.T("Farbe"), R = new Rectangle(ax, 11, 18, 18) }); ax += 24; }
                    attrButtons.Add(new Btn { Id = "cc", Tip = L.T("Eigene Farbe"), R = new Rectangle(ax, 11, 18, 18) }); ax += 24 + 16;
                }
                Action<string, string, int, int> add = (id, tip, w, after) => { attrButtons.Add(new Btn { Id = id, Tip = tip, R = new Rectangle(ax, 8, w, 24) }); ax += w + 4 + after; };
                if (widthOn) { add("w-", L.T("Dünner"), 24, 0); add("w+", L.T("Dicker"), 24, 44); }
                if (fillOn) add("fill", L.T("Füllung"), 62, 0);
                if (handOn) add("hand", L.T("Handgezeichnet"), 62, 0);
                if (fillOn || handOn) ax += 12;
                if (opacityOn) { add("o-", L.T("Weniger Deckkraft"), 24, 0); add("o+", L.T("Mehr Deckkraft"), 24, 44); }
                if (extras.Count > 0 && ax > 13) ax += 8;
                foreach (var ex in extras) add(ex.Item1, ex.Item3, ex.Item2, 0);
                if (attrButtons.Count > 0)
                {
                    int pw = ax + 9;
                    attrPanel = new Rectangle((cw - pw) / 2, BarH + 8, pw, 40);
                    foreach (var b in attrButtons) b.R.Offset(attrPanel.X, attrPanel.Y);
                }
            }

            // Backdrop choices, laid out like the color dots.
            backdropBtns.Clear();
            backdropPanel = Rectangle.Empty;
            if (backdropOpen)
            {
                var bb = buttons.First(b => b.Id == "backdrop");
                var anchor = bb.R.IsEmpty ? more.R : bb.R;
                int n = Renderer.Gradients.Length + 2, pw = n * 28 + 18;
                backdropPanel = new Rectangle(Math.Max(8, Math.Min(cw - pw - 8, anchor.X + anchor.Width / 2 - pw / 2)), (!attrPanel.IsEmpty ? attrPanel.Bottom : bb.R.Y >= BarH ? bb.R.Bottom : BarH) + 8, pw, 40); // below the attribute bar when both are open
                int bx = backdropPanel.X + 12;
                backdropBtns.Add(new Btn { Id = "bdoff", Tip = L.T("Aus"), R = new Rectangle(bx, backdropPanel.Y + 9, 22, 22) }); bx += 28;
                for (int i = 0; i < Renderer.Gradients.Length; i++) { backdropBtns.Add(new Btn { Id = "bd" + i, Tip = "Backdrop", R = new Rectangle(bx, backdropPanel.Y + 9, 22, 22) }); bx += 28; }
                backdropBtns.Add(new Btn { Id = "bdc", Tip = L.T("Eigene Farbe"), R = new Rectangle(bx, backdropPanel.Y + 9, 22, 22) });
            }
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
                    using (var br = new SolidBrush(c)) g.DrawString("12", f, br, cx, cy - 6, sf);
                    using (var p = new Pen(c, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(p, cx - 9, cy + 6, cx + 9, cy + 6);
                        g.DrawLine(p, cx - 9, cy + 3, cx - 9, cy + 9);
                        g.DrawLine(p, cx + 9, cy + 3, cx + 9, cy + 9);
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
                    using (var path = Renderer.Round(btn.R, Ui.ControlRadius)) using (var br = new SolidBrush(on ? Hover : Color.FromArgb(52, 52, 56))) g.FillPath(br, path);
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
                using (var path = Renderer.Round(morePanel, Ui.CardRadius)) using (var br = new SolidBrush(Color.FromArgb(40, 40, 43))) g.FillPath(br, path);
                using (var path = Renderer.Round(morePanel, Ui.CardRadius)) using (var p = new Pen(Color.FromArgb(70, 70, 75))) g.DrawPath(p, path);
                foreach (var btn in buttons)
                {
                    if (btn.R.IsEmpty || btn.R.Y < BarH) continue;
                    bool on = btn.Tool == tool || (btn.Id == "backdrop" && backdrop >= 0);
                    if (on || btn == hover)
                        using (var path = Renderer.Round(btn.R, Ui.ControlRadius)) using (var br = new SolidBrush(on ? Hover : Color.FromArgb(58, 58, 62))) g.FillPath(br, path);
                    DrawButton(g, btn);
                }
            }

            if (!backdropPanel.IsEmpty)
            {
                using (var path = Renderer.Round(backdropPanel, Ui.CardRadius)) using (var br = new SolidBrush(Color.FromArgb(240, 36, 36, 38))) g.FillPath(br, path);
                using (var path = Renderer.Round(backdropPanel, Ui.CardRadius)) using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(p, path);
                foreach (var btn in backdropBtns)
                {
                    bool cur = btn.Id == "bdoff" ? backdrop < 0 : btn.Id == "bdc" ? backdrop >= 0 && backdropColor.HasValue : backdrop == int.Parse(btn.Id.Substring(2)) && !backdropColor.HasValue;
                    var r = btn.R;
                    if (btn.Id == "bdoff")
                    {
                        using (var br = new SolidBrush(Color.FromArgb(52, 52, 56))) g.FillEllipse(br, r);
                        using (var p = new Pen(Color.FromArgb(160, 160, 166), 1.6f)) g.DrawLine(p, r.X + 6, r.Bottom - 6, r.Right - 6, r.Y + 6);
                    }
                    else
                    {
                        var cols = btn.Id == "bdc" ? Renderer.Custom(backdropColor ?? Color.FromArgb(70, 70, 76)) : Renderer.Gradients[int.Parse(btn.Id.Substring(2))];
                        using (var br = new LinearGradientBrush(r, cols[0], cols[1], 45f)) g.FillEllipse(br, r);
                        if (btn.Id == "bdc") using (var f = new Font("Segoe UI", 9f)) using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString("+", f, Renderer.Contrast(cols[0]) == Color.White ? Brushes.White : Brushes.Black, r, sf);
                    }
                    using (var p = new Pen(cur ? Color.White : btn == hover ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(60, 255, 255, 255), cur ? 2 : 1)) g.DrawEllipse(p, r);
                }
            }

            if (!attrPanel.IsEmpty)
            {
                using (var path = Renderer.Round(attrPanel, Ui.CardRadius)) using (var br = new SolidBrush(Color.FromArgb(240, 36, 36, 38))) g.FillPath(br, path);
                using (var path = Renderer.Round(attrPanel, Ui.CardRadius)) using (var p = new Pen(Color.FromArgb(40, 255, 255, 255))) g.DrawPath(p, path);
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
                        if (btn.Id == "mode") { DrawMode(g, btn.R, f, sf); continue; }
                        if (btn.Id == "strength") { DrawStrength(g, btn.R, f); continue; }
                        bool active = (btn.Id == "fill" && fill) || (btn.Id == "hand" && hand);
                        using (var path = Renderer.Round(btn.R, Ui.ControlRadius)) using (var br = new SolidBrush(active ? Hover : btn == hover ? Color.FromArgb(58, 58, 62) : Color.FromArgb(46, 46, 49))) g.FillPath(br, path);
                        string t = btn.Id == "w-" || btn.Id == "o-" ? "−" : btn.Id == "w+" || btn.Id == "o+" ? "+" : btn.Id == "fill" ? L.T("Füllung") : btn.Id == "hand" ? L.T("Skizze") : AttrLabel(btn.Id);
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
                string tip = hover.Id == "upload" && Busy("upload") ? L.T("Klicken zum Abbrechen") : hover.Tip;
                var parts = tip.Split(new[] { "  " }, StringSplitOptions.None);
                Glint.Chip.Draw(g, parts[0], area, parts.Length > 1 ? parts[1] : null);
            }
            DrawEffects(g);
        }

        Btn HitBtn(Point p)
        {
            foreach (var b in buttons) if (b.R.Contains(p)) return b;
            foreach (var b in attrButtons) if (b.R.Contains(p)) return b;
            foreach (var b in backdropBtns) if (b.R.Contains(p)) return b;
            return null;
        }
    }
}
