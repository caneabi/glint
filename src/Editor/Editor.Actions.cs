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
                case "backdrop": backdrop = backdrop + 1 >= Renderer.Gradients.Length ? -1 : backdrop + 1; Recompose(); break;
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
