using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glint
{
    class SettingsForm : ResizableForm
    {
        public static bool NoSave; // test hook
        const int SideW = 200, TopH = 36;
        static readonly string[] PageIcons = { "sun", "crop", "keyboard", "image", "volume-2", "download", "cloud-upload" };
        string[] PageNames;
        readonly List<SettingsPage> pages = new List<SettingsPage>();
        readonly Dictionary<string, KeyRecorder> boxes = new Dictionary<string, KeyRecorder>(), previewBoxes = new Dictionary<string, KeyRecorder>();
        Rectangle closeRect;
        int current, hoverItem = -1; bool hoverClose;

        public SettingsForm()
        {
            Icon = Brand.AppIcon;
            StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; Font = Ui.Font;
            MinimumSize = Ui.S(760, 640);
            FitToScreen(800, 680);
            closeRect = new Rectangle((int)(ClientSize.Width / Ui.F) - 42, 0, 42, TopH - 2);
            BuildPages();
            FormClosing += delegate
            {
                StoreKeys();
                if (!NoSave) Settings.Current.Save();
            };
        }

        void StoreKeys()
        {
            var s = Settings.Current;
            foreach (var b in boxes) s.Hotkeys[b.Key] = b.Value.Value;
            foreach (var b in previewBoxes) s.PreviewKeys[b.Key] = b.Value.Value;
        }

        // Rebuilds every page in the current language, the window stays open.
        public void Relocalize()
        {
            StoreKeys();
            int page = current;
            SuspendLayout();
            foreach (var old in pages) { Controls.Remove(old); old.Dispose(); }
            pages.Clear(); boxes.Clear(); previewBoxes.Clear();
            BuildPages();
            SelectPage(page);
            ResumeLayout();
            if (Tray.Instance != null) Tray.Instance.RefreshMenu();
        }

        void BuildPages()
        {
            Text = L.T("Glint – Einstellungen");
            PageNames = new[] { L.T("Allgemein"), L.T("Aufnahme"), L.T("Tastenkürzel"), L.T("Schwebendes Fenster"), L.T("Töne"), L.T("Ausgabe"), "Upload" };
            var s = Settings.Current;
            var size = new Size(ClientSize.Width - Ui.S(SideW), ClientSize.Height - Ui.S(TopH));

            var p = NewPage(L.T("Allgemein"), size, L.T("Wie Glint gestartet wird und läuft."));
            var lang = new Segmented(new[] { "auto", "de", "en" }, new[] { "Auto", "Deutsch", "English" }, s.Language, 230);
            lang.Changed = () =>
            {
                s.Language = lang.Value;
                s.Save();
                L.Init(s.Language);
                BeginInvoke(new Action(Relocalize)); // this control gets replaced too
            };
            p.AddRow(L.T("Sprache"), lang);
            var auto = new Toggle(Installer.Autostart);
            auto.Changed = () =>
            {
                try { Installer.SetAutostart(auto.On); }
                catch (Exception ex) { Toast.Error(L.T("Autostart konnte nicht geändert werden"), "Autostart", ex); }
            };
            p.AddRow(L.T("Mit Windows starten"), auto);
            var tray = new Toggle(s.ShowTray);
            tray.Changed = () => { s.ShowTray = tray.On; s.Save(); if (Tray.Instance != null) Tray.Instance.ApplyTrayVisibility(); };
            p.AddRow(L.T("Tray-Symbol anzeigen"), tray);
            var startTray = new Toggle(s.StartInTray);
            startTray.Changed = () => { s.StartInTray = startTray.On; s.Save(); };
            p.AddRow(L.T("Beim Start im Tray bleiben"), startTray);
            var updates = new Toggle(s.AutoUpdate);
            updates.Changed = () => { s.AutoUpdate = updates.On; s.Save(); };
            p.AddRow(L.T("Automatisch nach Updates suchen"), updates);
            var channel = new Segmented(new[] { "stable", "nightly" }, new[] { "Stable", "Nightly" }, s.UpdateChannel, 150);
            channel.Changed = () => { s.UpdateChannel = channel.Value; s.Save(); };
            p.AddRow(L.T("Update-Kanal"), channel);
            var checkNow = new PillButton(L.T("Jetzt suchen"), 110);
            checkNow.Changed = () => Updater.Check(true);
            p.AddRow(L.T("Version ") + Updater.CurrentText, checkNow);
            var logs = new PillButton(L.T("Öffnen"), 110);
            logs.Changed = Log.Open;
            p.AddRow(L.T("Logs öffnen"), logs);
            var setup = new PillButton(L.T("Öffnen"), 110);
            setup.Changed = () => { Close(); if (Tray.Instance != null) Tray.Instance.RunSetupSoon(); };
            p.AddRow(L.T("Einrichtung"), setup);
            var quit = new PillButton(L.T("Beenden…"), 110);
            quit.Changed = () => { Close(); if (Tray.Instance != null) Tray.Instance.Quit(); };
            p.AddRow(L.T("Glint beenden"), quit);

            p = NewPage(L.T("Aufnahme"), size, L.T("Wie Aufnahmen ablaufen."));
            var after = new Segmented(new[] { "thumbnail", "editor", "copy" }, new[] { L.T("Fenster"), "Editor", L.T("Kopieren") }, s.After, 230);
            after.Changed = () => s.After = after.Value;
            p.AddRow(L.T("Nach der Aufnahme"), after);
            var snap = new Toggle(s.Snap); snap.Changed = () => s.Snap = snap.On;
            p.AddRow(L.T("Fenster-Snap"), snap);
            var freeze = new Toggle(s.Freeze); freeze.Changed = () => s.Freeze = freeze.On;
            p.AddRow(L.T("Bildschirm einfrieren"), freeze);
            var cross = new Toggle(s.Crosshair); cross.Changed = () => s.Crosshair = cross.On;
            p.AddRow(L.T("Großes Fadenkreuz"), cross);
            var color = new Segmented(new[] { "hex", "rgb", "oklch" }, new[] { "HEX", "RGB", "OKLCH" }, s.ColorFormat, 230);
            color.Changed = () => s.ColorFormat = color.Value;
            p.AddRow(L.T("Farbformat"), color);
            string[] heights = { "20000", "50000", "100000", "200000" };
            string nearest = heights.OrderBy(h => Math.Abs(int.Parse(h) - s.MaxScroll)).First();
            var scroll = new Segmented(heights, new[] { "20k", "50k", "100k", "200k" }, nearest, 230);
            scroll.Changed = () => s.MaxScroll = int.Parse(scroll.Value);
            p.AddRow(L.T("Max. Scroll-Höhe"), scroll);
            p.AddBlock(L.T("Hintergrund bei Fensteraufnahmen"), new WindowBgPicker(), 134);

            p = NewPage(L.T("Tastenkürzel"), size, L.T("Klicken und Taste drücken. Gilt überall in Windows."));
            foreach (var kv in Settings.Labels) AddRecorder(p, boxes, kv.Key, L.T(kv.Value), s.Hotkeys.ContainsKey(kv.Key) ? s.Hotkeys[kv.Key] : "");

            p = NewPage(L.T("Schwebendes Fenster"), size, L.T("Diese Tasten gelten, solange ein schwebendes Fenster offen ist."));
            var global = new Toggle(s.GlobalKeys);
            global.Changed = () => { s.GlobalKeys = global.On; s.Save(); };
            p.AddRow(L.T("Auch in anderen Apps reagieren"), global);
            var mainScreen = new Toggle(s.PreviewOnMain);
            mainScreen.Changed = () => { s.PreviewOnMain = mainScreen.On; s.Save(); };
            p.AddRow(L.T("Immer auf dem Hauptbildschirm"), mainScreen);
            foreach (var kv in Settings.PreviewLabels)
                AddRecorder(p, previewBoxes, kv.Key, L.T(kv.Value.Substring(kv.Value.IndexOf(": ") + 2)), s.PreviewKeys.ContainsKey(kv.Key) ? s.PreviewKeys[kv.Key] : "");

            p = NewPage(L.T("Töne"), size, L.T("Ein Klick auf den Lautsprecher spielt den Ton ab."));
            AddSound(p, L.T("Screenshot"), Sounds.Kind.Screenshot, s.SoundScreenshot, v => s.SoundScreenshot = v);
            AddSound(p, L.T("Upload abgeschlossen"), Sounds.Kind.Upload, s.SoundUpload, v => s.SoundUpload = v);
            AddSound(p, L.T("Kopiert"), Sounds.Kind.Copy, s.SoundCopy, v => s.SoundCopy = v);

            p = NewPage(L.T("Ausgabe"), size, L.T("Wie Bilder gespeichert und kopiert werden."));
            var folder = new PillButton(s.SaveDir, 280);
            folder.Changed = () =>
            {
                using (var d = new FolderBrowserDialog { SelectedPath = s.SaveDir })
                    if (d.ShowDialog(this) == DialogResult.OK) { s.SaveDir = d.SelectedPath; folder.Caption = s.SaveDir; folder.Invalidate(); }
            };
            p.AddStretch(L.T("Speicherordner"), folder);
            var fmt = new Segmented(new[] { "auto", "png", "jpg", "webp" }, new[] { "Auto", "PNG", "JPEG", "WebP" }, s.Format, 230);
            fmt.Changed = () => s.Format = fmt.Value;
            p.AddRow(L.T("Dateiformat"), fmt);
            var quality = new Slider(10, 100, 5, s.JpegQuality, 230);
            quality.Changed = () => s.JpegQuality = quality.Value;
            p.AddRow(L.T("JPEG-Qualität"), quality);
            var scale = new Segmented(new[] { "100", "75", "50", "25" }, new[] { "100 %", "75 %", "50 %", "25 %" }, s.OutputScale.ToString(), 230);
            scale.Changed = () => s.OutputScale = int.Parse(scale.Value);
            p.AddRow(L.T("Ausgabegröße"), scale);
            p.AddBlock(L.T("Dateiname beim Speichern"), new FileNameEditor(s.FileNameSave, false, ".png", v => s.FileNameSave = v), 158);
            p.AddBlock(L.T("Dateiname beim Hochladen"), new FileNameEditor(s.FileNameUpload, true, ".png", v => s.FileNameUpload = v), 158);

            uploadPage = NewPage("Upload", size, L.T("Der Schalter wählt das aktive Ziel, Hotkeys laden direkt hoch."));
            BuildUploadPage();

            SelectPage(0);
        }

        SettingsPage uploadPage;

        void AddSound(SettingsPage page, string label, Sounds.Kind kind, bool on, Action<bool> set)
        {
            var preview = new IconButton("volume-2", 24);
            preview.Changed = () => Sounds.Preview(kind);
            var toggle = new Toggle(on);
            toggle.Changed = () => { set(toggle.On); Settings.Current.Save(); };
            page.AddRow(label, preview, toggle);
        }

        // Imported settings open in the dialog first, nothing is saved or sent before that.
        Destination ImportShareX(out string notice)
        {
            notice = null;
            using (var dlg = new OpenFileDialog { Filter = "ShareX|*.sxcu;*.json" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                try
                {
                    var r = ShareXImport.Load(dlg.FileName);
                    var parts = new List<string>();
                    if (!r.ImageCapable) parts.Add(L.T("Datei ist nicht für Bilder gedacht."));
                    if (r.Unsupported.Count > 0) parts.Add(L.T("Nicht unterstützt: ") + string.Join(" ", r.Unsupported));
                    notice = parts.Count > 0 ? string.Join(" ", parts) : L.T("Importiert. Prüfe die Werte vor dem Speichern.");
                    return r.Target;
                }
                catch (Exception ex)
                {
                    Toast.Error(L.T("ShareX-Datei konnte nicht gelesen werden"), "ShareX-Import", ex);
                    return null;
                }
            }
        }

        void BuildUploadPage()
        {
            var s = Settings.Current;
            uploadPage.Reset();
            var active = Uploader.Active;
            foreach (var dest in s.Destinations)
            {
                var d = dest;
                var on = new Toggle(d == active);
                on.Changed = () => { s.ActiveDestination = d.Id; s.Save(); BeginInvoke(new Action(BuildUploadPage)); };
                var rec = new KeyRecorder(d.Hotkey) { Width = Ui.S(118), Height = Ui.S(28) };
                rec.Recorded = () => { d.Hotkey = rec.Value; s.Save(); };
                var clear = new IconButton("x", 24);
                clear.Changed = () => { rec.Value = ""; d.Hotkey = ""; s.Save(); };
                var edit = new IconButton("pencil", 24);
                edit.Changed = () =>
                {
                    var copy = d.Clone();
                    using (var f = DestinationForm.Open(copy))
                        if (f.ShowDialog(this) == DialogResult.OK)
                        {
                            foreach (var field in Destination.Fields) d.Set(field, copy.Get(field));
                            s.Save();
                            BeginInvoke(new Action(BuildUploadPage));
                        }
                };
                var del = new IconButton("trash-2", 24);
                del.Changed = () =>
                {
                    if (MessageBox.Show(this, L.T("Ziel „") + d.Name + L.T("“ samt Zugangsdaten entfernen? Hochgeladene Dateien bleiben erhalten."), "Glint", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    s.Destinations.Remove(d);
                    s.Save();
                    BeginInvoke(new Action(BuildUploadPage));
                };
                uploadPage.AddRow(d.Name + "  ·  " + d.TypeName, rec, clear, edit, del, on);
            }
            var names = new[] { "Zipline", "R2", "S3", "Discord", L.T("Eigene"), "ShareX" };
            var types = new[] { "zipline", "r2", "s3", "discord", "custom", "sharex" };
            var pills = new Control[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string type = types[i];
                var pill = new PillButton(names[i], 64);
                pill.Changed = () =>
                {
                    string notice = null;
                    var d = type == "sharex" ? ImportShareX(out notice) : new Destination { Type = type };
                    if (d == null) return;
                    using (var f = DestinationForm.Open(d))
                    {
                        var custom = f as CustomDestinationForm;
                        if (custom != null && notice != null) custom.ShowNotice(notice);
                        if (f.ShowDialog(this) == DialogResult.OK)
                        {
                            s.Destinations.Add(d);
                            if (string.IsNullOrEmpty(s.ActiveDestination)) s.ActiveDestination = d.Id;
                            s.Save();
                            BeginInvoke(new Action(BuildUploadPage));
                        }
                    }
                };
                pills[i] = pill;
            }
            uploadPage.AddRow(L.T("Neu"), pills);
        }

        SettingsPage NewPage(string title, Size size, string subtitle)
        {
            var p = new SettingsPage(title, size, subtitle) { Location = new Point(Ui.S(SideW), Ui.S(TopH)), Visible = false };
            pages.Add(p); Controls.Add(p);
            return p;
        }

        void AddRecorder(SettingsPage page, Dictionary<string, KeyRecorder> group, string key, string label, string value)
        {
            var rec = new KeyRecorder(value) { Width = Ui.S(170), Height = Ui.S(28) };
            rec.Recorded = () =>
            {
                // One key, one action per group.
                foreach (var other in group.Values) if (other != rec && other.Value == rec.Value) other.Value = "";
            };
            var clear = new IconButton("x", 24);
            clear.Changed = () => rec.Value = "";
            group[key] = rec;
            page.AddRow(label, rec, clear);
        }

        public void SelectPage(int i)
        {
            current = i;
            for (int k = 0; k < pages.Count; k++) pages[k].Visible = k == i;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            closeRect = new Rectangle((int)(ClientSize.Width / Ui.F) - 42, 0, 42, TopH - 2);
            foreach (var p in pages) p.Size = new Size(ClientSize.Width - Ui.S(SideW), ClientSize.Height - Ui.S(TopH));
            Invalidate();
        }

        static Point Logical(Point p) { return new Point((int)(p.X / Ui.F), (int)(p.Y / Ui.F)); }

        protected override bool IsControlAt(Point p)
        {
            var l = Logical(p);
            for (int i = 0; i < PageNames.Length; i++) if (ItemRect(i).Contains(l)) return true;
            return closeRect.Contains(l);
        }

        Rectangle ItemRect(int i) { return new Rectangle(12, 56 + i * 38, SideW - 24, 34); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            Ui.Hairline(g, SideW - 1, 0, SideW - 1, (int)(ClientSize.Height / Ui.F));
            Brand.DrawMark(g, new Rectangle(22, 19, 16, 16), Ui.Text, Brand.Pixel);
            Ui.Prepare(g);
            Brand.DrawWordmark(g, 45, 22, 9.5f, Ui.Text, Brand.Pixel);
            Ui.Prepare(g);
            for (int i = 0; i < PageNames.Length; i++)
            {
                var r = ItemRect(i);
                if (i == current) Ui.Pill(g, r, Ui.Card, Ui.ControlRadius);
                else if (i == hoverItem) Ui.Pill(g, r, Ui.Side, Ui.ControlRadius);
                Icons.DrawLucide(g, PageIcons[i], r.X + 20, r.Y + r.Height / 2f, 16, 1.4f, i == current ? Ui.Text : Ui.Dim);
                using (var b = new SolidBrush(i == current ? Ui.Text : Ui.Dim))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                    g.DrawString(PageNames[i], Ui.Small, b, new RectangleF(r.X + 36, r.Y, r.Width - 40, r.Height), sf);
                if (i == current) using (var b = new SolidBrush(Brand.Pixel)) g.FillRectangle(b, r.Right - 14, r.Y + r.Height / 2 - 3, 6, 6);
            }
            if (hoverClose) using (var b = new SolidBrush(Color.FromArgb(232, 17, 35))) g.FillRectangle(b, closeRect);
            Icons.DrawLucide(g, "x", closeRect.X + closeRect.Width / 2f, closeRect.Height / 2f, 15, 1.3f, hoverClose ? Ui.Text : Ui.Dim);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            var l = Logical(e.Location);
            for (int i = 0; i < PageNames.Length; i++) if (ItemRect(i).Contains(l)) h = i;
            bool c = closeRect.Contains(l);
            if (h != hoverItem || c != hoverClose) { hoverItem = h; hoverClose = c; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverItem = -1; hoverClose = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var l = Logical(e.Location);
            if (closeRect.Contains(l)) { Close(); return; }
            for (int i = 0; i < PageNames.Length; i++) if (ItemRect(i).Contains(l)) { SelectPage(i); return; }
        }

        protected override bool ProcessCmdKey(ref Message m, Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref m, k);
        }
    }
}
