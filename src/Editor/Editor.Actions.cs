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
    // What the buttons and shortcuts do with the image.
    public partial class Editor
    {
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
            if (Busy("upload")) { if (uploadCancel != null) uploadCancel.Cancel(); return; } // a second click cancels
            if (d == null) { Effect("upload", ButtonFx.State.Fail, L.T("Kein Upload-Ziel eingerichtet")); return; }
            Effect("upload", ButtonFx.State.Busy);
            uploadCancel = new System.Threading.CancellationTokenSource();
            try
            {
                bool copied;
                using (var x = Export()) copied = await Uploader.Upload(x, d, uploadCancel.Token);
                if (!IsDisposed) Effect("upload", ButtonFx.State.Done, copied ? null : L.T("Hochgeladen, Link steht im Log"));
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) { fx.Remove("upload"); Note(L.T("Upload abgebrochen"), 2000); }
            }
            catch (Exception ex)
            {
                Log.Error("Upload nach " + d.Name + " (" + d.TypeName + ")", ex);
                if (!IsDisposed) Effect("upload", ButtonFx.State.Fail, L.T("Upload fehlgeschlagen"));
            }
            finally { uploadCancel.Dispose(); uploadCancel = null; }
        }

        void ClickBtn(Btn b)
        {
            if (b.Id == "more") { moreOpen = !moreOpen; Invalidate(); return; }
            moreOpen = false;
            if (b.Tool.HasValue) { SetTool(b.Tool.Value); return; }
            switch (b.Id)
            {
                case "undo": Undo(); break;
                case "redo": Redo(); break;
                case "backdrop": BackdropMenu(b); return;
                case "font": FontMenu(Below(b.R)); return;
                case "shape": ShapeMenu(Below(b.R)); return;
                case "zoom": ZoomMenu(Below(b.R)); return;
                case "ocr": RunOcr(); break;
                case "addcap": AddCapture(); break;
                case "pin": new PinWindow(Export()).Show(); Close(); break;
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

        Point Below(Rectangle r) { return PointToScreen(new Point(r.X, r.Bottom + 4)); }

        void BackdropMenu(Btn b)
        {
            var items = new List<ToolStripItem> { MenuKit.Check(L.T("Aus"), backdrop < 0, () => { backdrop = -1; Recompose(); }) };
            for (int i = 0; i < Renderer.Gradients.Length; i++)
            {
                int k = i;
                var it = MenuKit.Check(L.T("Verlauf") + " " + (i + 1), backdrop == i && !backdropColor.HasValue, () => { backdrop = k; backdropColor = null; Recompose(); });
                it.Image = MenuKit.Swatch(Renderer.Gradients[i][0], Renderer.Gradients[i][1]);
                items.Add(it);
            }
            var own = MenuKit.Check(L.T("Eigene Farbe…"), backdropColor.HasValue, () =>
            {
                using (var d = new ColorPicker(backdropColor ?? Color.FromArgb(64, 68, 72)))
                    if (d.ShowDialog(this) == DialogResult.OK) { backdropColor = d.Value; backdrop = 0; Recompose(); }
            });
            if (backdropColor.HasValue) own.Image = MenuKit.Swatch(backdropColor.Value, Renderer.Custom(backdropColor.Value)[1]);
            items.Add(new ToolStripSeparator()); items.Add(own);
            MenuKit.Popup(Below(b.R), items.ToArray());
        }

        static readonly string[] FontChoices = { "Segoe UI", "Arial", "Georgia", "Consolas", "Impact", "Comic Sans MS", "Segoe Print" };

        ToolStripMenuItem[] FontItems()
        {
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name));
            string current = sel != null && sel.Kind == Kind.Text ? sel.Font : textFont;
            return FontChoices.Where(installed.Contains).Select(f =>
            {
                var it = MenuKit.Check(f, f == current, () => { textFont = f; Apply(o => { if (o.Kind == Kind.Text) o.Font = f; }); });
                it.Font = new Font(f, 9.5f);
                return it;
            }).ToArray();
        }

        void FontMenu(Point at) { MenuKit.Popup(at, FontItems()); }

        ToolStripMenuItem[] ShapeItems()
        {
            int current = sel != null && sel.Kind == Kind.Magnifier ? sel.Shape : magShape;
            return Enumerable.Range(0, ShapeNames.Length).Select(i => MenuKit.Check(L.T(ShapeNames[i]), i == current, () => { magShape = i; Apply(o => { if (o.Kind == Kind.Magnifier) o.Shape = i; }); })).ToArray();
        }

        ToolStripMenuItem[] ZoomItems()
        {
            int current = sel != null && sel.Kind == Kind.Magnifier ? sel.Zoom : magZoom;
            return new[] { 2, 3, 4, 6 }.Select(z => MenuKit.Check(z + "×", z == current, () => { magZoom = z; Apply(o => { if (o.Kind == Kind.Magnifier) o.Zoom = z; }); })).ToArray();
        }

        void ShapeMenu(Point at) { MenuKit.Popup(at, ShapeItems()); }
        void ZoomMenu(Point at) { MenuKit.Popup(at, ZoomItems()); }

        ToolStripMenuItem SubMenu(string text, ToolStripMenuItem[] items)
        {
            var sub = MenuKit.Sub(text, true);
            sub.DropDownItems.AddRange(items);
            return sub;
        }

        // Right click on the canvas: object actions, or the image actions on empty space.
        void CanvasMenu(Point at)
        {
            var screen = PointToScreen(at);
            var o = HitObject(ToImage(at));
            if (o == null)
            {
                MenuKit.Popup(screen,
                    MenuKit.Item(L.T("Kopieren"), CopyImage, "copy"), MenuKit.Item(L.T("Speichern"), Save, "save"), MenuKit.Item(L.T("Speichern unter…"), SaveAs),
                    new ToolStripSeparator(),
                    MenuKit.Item(L.T("Anpinnen"), () => { new PinWindow(Export()).Show(); Close(); }, "pin"),
                    MenuKit.Item(L.T("Hochladen"), () => RunUpload(Uploader.Active), "cloud-upload"),
                    MenuKit.Item(L.T("Text erkennen"), RunOcr, "scan-text"),
                    Clipboard.ContainsImage() ? MenuKit.Item(L.T("Einfügen"), () => { using (var i = Clipboard.GetImage()) AddImage(Imaging.Normalize(i)); }, "clipboard") : null);
                return;
            }
            sel = o; tool = Tool.Select; Invalidate();
            var items = new List<ToolStripItem>();
            if (o.Kind == Kind.Text) { items.Add(MenuKit.Item(L.T("Bearbeiten"), () => StartText(o.A, o), "pencil")); items.Add(SubMenu(L.T("Schriftart"), FontItems())); }
            if (o.Kind == Kind.Magnifier) { items.Add(SubMenu(L.T("Form"), ShapeItems())); items.Add(SubMenu("Zoom", ZoomItems())); }
            var colors = MenuKit.Sub(L.T("Farbe"), true);
            foreach (var c in Palette) { var col = c; var it = MenuKit.Check(Colors.Hex(col), o.Color.ToArgb() == col.ToArgb(), () => SetColor(col)); it.Image = MenuKit.Swatch(col, col); colors.DropDownItems.Add(it); }
            colors.DropDownItems.Add(new ToolStripSeparator());
            colors.DropDownItems.Add(MenuKit.Item(L.T("Eigene Farbe…"), () => { using (var d = new ColorPicker(o.Color)) if (d.ShowDialog(this) == DialogResult.OK) SetColor(d.Value); }));
            items.Add(colors);
            items.Add(new ToolStripSeparator());
            items.Add(MenuKit.Item(L.T("Duplizieren"), () => { doc.Push(); var c = o.Clone(); c.Move(16, 16); Objs.Add(c); sel = c; Recompose(); }, "copy"));
            items.Add(MenuKit.Item(L.T("Nach vorne"), () => { doc.Push(); Objs.Remove(o); Objs.Add(o); Recompose(); }));
            items.Add(MenuKit.Item(L.T("Nach hinten"), () => { doc.Push(); Objs.Remove(o); Objs.Insert(0, o); Recompose(); }));
            items.Add(new ToolStripSeparator());
            items.Add(MenuKit.Item(L.T("Löschen"), () => { doc.Push(); Objs.Remove(o); sel = null; Recompose(); }, "trash-2"));
            MenuKit.Popup(screen, items.ToArray());
        }

        // Corner handles scale the text, the opposite corner stays put.
        void ScaleText(PointF p)
        {
            var r = sel.Box;
            bool left = handle == 0 || handle == 3, top = handle <= 1;
            var anchor = new PointF(left ? r.Right : r.Left, top ? r.Bottom : r.Top);
            float oldSize = Renderer.TextSize(sel), h = Math.Max(6, Math.Abs(p.Y - anchor.Y));
            float size = Math.Max(6, Math.Min(400, oldSize * h / Math.Max(1, r.Height))), k = size / oldSize;
            sel.Size = size;
            sel.A = new PointF(left ? anchor.X - r.Width * k : anchor.X, top ? anchor.Y - r.Height * k : anchor.Y);
        }

        void SetColor(Color c) { color = c; Apply(o => o.Color = c); }

        void SetWidth(float w) { width = w; Apply(o => o.Width = w); }

        void SetOpacity(int v) { opacity = v; Apply(o => o.Opacity = v); }

        void Apply(Action<Obj> a)
        {
            if (sel == null) { Invalidate(); return; }
            doc.Push(); a(sel); Recompose();
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
            using (var d = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg|WebP|*.webp", FileName = FileNames.ForSave(doc.Base.Width, doc.Base.Height), InitialDirectory = Settings.Current.SaveDir })
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
            doc.Push();
            float x = Math.Max(0, (doc.Base.Width - img.Width) / 2f), y = Math.Max(0, (doc.Base.Height - img.Height) / 2f);
            sel = new Obj { Kind = Kind.Image, Img = img, A = new PointF(x, y), B = new PointF(x + img.Width, y + img.Height) };
            Objs.Add(sel);
            tool = Tool.Select; Recompose();
        }

        void ResizeDialog()
        {
            using (var f = new Form { Text = L.T("Bildgröße"), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(260, 90), BackColor = Bar, ForeColor = Color.White })
            {
                f.Controls.Add(new Label { Text = L.T("Breite in Pixeln"), Left = 12, Top = 15, Width = 110 });
                var n = new NumericUpDown { Left = 130, Top = 12, Width = 110, Minimum = 16, Maximum = 20000, Value = Math.Min(20000, Math.Max(16, doc.Base.Width)) };
                var ok = new Button { Text = "OK", Left = 165, Top = 50, Width = 75, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat };
                f.Controls.Add(n); f.Controls.Add(ok); f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK) return;
                doc.Resize((int)n.Value);
                sel = null; fit = true; Recompose();
            }
        }

        void CopyColor(Color c)
        {
            prevPicked = picked; picked = c;
            string t = Colors.Format(c, Settings.Current.ColorFormat);
            Clipboard.SetText(t);
            Sounds.Play(Sounds.Kind.Pick);
            statusNote = L.T("Kopiert: ") + t;
            if (prevPicked.HasValue)
                statusNote += string.Format(L.T("   ·   Kontrast zu {0}: {1:0.00}:1 (WCAG)   Lc {2:0} (APCA)"), Colors.Hex(prevPicked.Value), Colors.Wcag(c, prevPicked.Value), Colors.Apca(c, prevPicked.Value));
            Note(statusNote, prevPicked.HasValue ? 3500 : 1600);
            Invalidate();
        }
    }
}
