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
        static readonly string[] PageIcons = { "sun", "crop", "keyboard", "image", "volume-2", "download", "cloud-upload", "info" };
        string[] PageNames;
        readonly List<SettingsPage> pages = new List<SettingsPage>();
        readonly Dictionary<string, KeyRecorder> boxes = new Dictionary<string, KeyRecorder>(), previewBoxes = new Dictionary<string, KeyRecorder>(), panelBoxes = new Dictionary<string, KeyRecorder>();
        readonly Dictionary<KeyRecorder, Destination> destRecorders = new Dictionary<KeyRecorder, Destination>();
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
            foreach (var b in panelBoxes) s.PanelKeys[b.Key] = b.Value.Value;
        }

        // Rebuilds every page in the current language, the window stays open.
        public void Relocalize()
        {
            StoreKeys();
            int page = current;
            SuspendLayout();
            foreach (var old in pages) { var host = old.Parent; Controls.Remove(host); host.Dispose(); }
            pages.Clear(); boxes.Clear(); previewBoxes.Clear(); panelBoxes.Clear(); destRecorders.Clear();
            BuildPages();
            SelectPage(page);
            ResumeLayout();
            if (Tray.Instance != null) Tray.Instance.RefreshMenu();
        }

        void BuildPages()
        {
            Text = L.T("Glint – Einstellungen");
            PageNames = new[] { L.T("Allgemein"), L.T("Aufnahme"), L.T("Tastenkürzel"), L.T("Schwebendes Fenster"), L.T("Töne"), L.T("Ausgabe"), "Upload", "Info" };
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
            var color = new Segmented(Colors.Formats, new[] { "HEX", "RGB", "HSL", "HSB", "OKLCH" }, s.ColorFormat, 300);
            color.Changed = () => s.ColorFormat = color.Value;
            p.AddRow(L.T("Farbformat"), color);
            var colorAfter = new Segmented(new[] { "panel", "copy" }, new[] { "Panel", L.T("Nur kopieren") }, s.ColorAfter, 230);
            colorAfter.Changed = () => s.ColorAfter = colorAfter.Value;
            p.AddRow(L.T("Nach Farbaufnahme"), colorAfter);
            string[] heights = { "20000", "50000", "100000", "200000" };
            string nearest = heights.OrderBy(h => Math.Abs(int.Parse(h) - s.MaxScroll)).First();
            var scroll = new Segmented(heights, new[] { "20k", "50k", "100k", "200k" }, nearest, 230);
            scroll.Changed = () => s.MaxScroll = int.Parse(scroll.Value);
            p.AddRow(L.T("Max. Scroll-Höhe"), scroll);
            p.AddBlock(L.T("Hintergrund bei Fensteraufnahmen"), new WindowBgPicker(), 134);

            p = NewPage(L.T("Tastenkürzel"), size, L.T("Klicken und Taste drücken. Gilt überall in Windows."));
            foreach (var kv in Settings.Labels) AddRecorder(p, boxes, kv.Key, L.T(kv.Value), s.Hotkeys.ContainsKey(kv.Key) ? s.Hotkeys[kv.Key] : "", true);
            p.BeginGroup();
            foreach (var kv in Settings.PanelLabels) AddRecorder(p, panelBoxes, kv.Key, L.T(kv.Value), s.PanelKeys.ContainsKey(kv.Key) ? s.PanelKeys[kv.Key] : "");

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
            p.AddRow(L.T("Dateiformat"), fmt);
            var quality = new Slider(10, 100, 5, s.JpegQuality, 230);
            quality.Changed = () => s.JpegQuality = quality.Value;
            p.AddRow(L.T("JPEG-Qualität"), quality);
            quality.Enabled = s.Format == "auto" || s.Format == "jpg"; // auto can pick JPEG
            fmt.Changed = () => { s.Format = fmt.Value; quality.Enabled = fmt.Value == "auto" || fmt.Value == "jpg"; };
            var scale = new Segmented(new[] { "100", "75", "50", "25" }, new[] { "100 %", "75 %", "50 %", "25 %" }, s.OutputScale.ToString(), 230);
            scale.Changed = () => s.OutputScale = int.Parse(scale.Value);
            p.AddRow(L.T("Ausgabegröße"), scale);
            p.AddBlock(L.T("Dateiname beim Speichern"), new FileNameEditor(s.FileNameSave, false, ".png", v => s.FileNameSave = v), 104);
            p.AddBlock(L.T("Dateiname beim Hochladen"), new FileNameEditor(s.FileNameUpload, true, ".png", v => s.FileNameUpload = v), 104);
            p.AddBlock(L.T("Variablen"), new FileNameVars(), 124);

            uploadPage = NewPage("Upload", size, L.T("Der Schalter wählt das aktive Ziel, Hotkeys laden direkt hoch."));
            BuildUploadPage();

            p = NewPage("Info", size, L.T("Version, Logs und Einrichtung."));
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
                rec.Recorded = () => { d.Hotkey = rec.Value; s.Save(); CheckGlobal(rec); };
                rec.Taken = Tray.Instance != null && Tray.Instance.Failed.Contains("upload:" + d.Id);
                destRecorders[rec] = d;
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
                uploadPage.AddRow(d.Name + "  ·  " + d.TypeName, rec, edit, del, on);
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
            if (uploadPage.Parent != null) Fit(uploadPage);
        }

        SettingsPage NewPage(string title, Size size, string subtitle)
        {
            // Each page scrolls inside its own host when it's taller than the window.
            var host = new PassPanel { Location = new Point(Ui.S(SideW), Ui.S(TopH)), Size = size, AutoScroll = true, Visible = false };
            var p = new SettingsPage(title, size, subtitle) { Location = Point.Empty };
            host.Controls.Add(p);
            pages.Add(p); Controls.Add(host);
            return p;
        }

        static void Fit(SettingsPage p)
        {
            var host = p.Parent;
            p.Height = Math.Max(p.ContentHeight, host.ClientSize.Height);
            p.Width = host.ClientSize.Width;
        }

        // Global hotkeys and upload hotkeys share one pool, a key can only do one of them.
        void CheckGlobal(KeyRecorder rec)
        {
            var s = Settings.Current;
            string moved = null;
            if (rec.Value != "")
            {
                foreach (var kv in boxes)
                    if (kv.Value != rec && kv.Value.Value == rec.Value) { kv.Value.Value = ""; kv.Value.Taken = false; moved = L.T(Settings.Labels[kv.Key]); }
                foreach (var kv in destRecorders)
                    if (kv.Key != rec && kv.Key.Value == rec.Value) { kv.Key.Value = ""; kv.Key.Taken = false; kv.Value.Hotkey = ""; s.Save(); moved = "Upload " + kv.Value.Name; }
            }
            bool ours = s.Hotkeys.ContainsValue(rec.Value) || s.Destinations.Any(d => d.Hotkey == rec.Value);
            rec.Taken = rec.Value != "" && !ours && Tray.Instance != null && Tray.Instance.IsTaken(rec.Value);
            rec.Invalidate();
            if (rec.Taken) ShowBanner(L.T("Diese Tasten nutzt schon Windows oder eine andere App"), 4000, true, null);
            else if (moved != null) ShowBanner(string.Format(L.T("Bei „{0}“ entfernt"), moved), 3000, false, null);
        }

        void AddRecorder(SettingsPage page, Dictionary<string, KeyRecorder> group, string key, string label, string value, bool global = false)
        {
            var rec = new KeyRecorder(value) { Width = Ui.S(170), Height = Ui.S(28) };
            if (global)
            {
                rec.Taken = Tray.Instance != null && Tray.Instance.Failed.Contains(key);
                rec.Recorded = () => CheckGlobal(rec);
                group[key] = rec;
                page.AddRow(label, rec);
                return;
            }
            rec.Recorded = () =>
            {
                // One key, one action per group.
                if (rec.Value == "") return;
                foreach (var other in group.Values) if (other != rec && other.Value == rec.Value) other.Value = "";
            };
            group[key] = rec;
            page.AddRow(label, rec);
        }

        public void SelectPage(int i)
        {
            current = i;
            for (int k = 0; k < pages.Count; k++) pages[k].Parent.Visible = k == i;
            Fit(pages[i]);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            closeRect = new Rectangle((int)(ClientSize.Width / Ui.F) - 42, 0, 42, TopH - 2);
            foreach (var p in pages) { p.Parent.Size = new Size(ClientSize.Width - Ui.S(SideW), ClientSize.Height - Ui.S(TopH)); Fit(p); }
            Invalidate();
        }

        static Point Logical(Point p) { return new Point((int)(p.X / Ui.F), (int)(p.Y / Ui.F)); }

        protected override bool IsControlAt(Point p)
        {
            var l = Logical(p);
            for (int i = 0; i < PageNames.Length; i++) if (ItemRect(i).Contains(l)) return true;
            return closeRect.Contains(l);
        }

        // The last page sits at the bottom of the sidebar like a footer.
        bool IsFooter(int i) { return i == PageNames.Length - 1; }
        Rectangle ItemRect(int i)
        {
            if (IsFooter(i)) return new Rectangle(12, (int)(ClientSize.Height / Ui.F) - 46, SideW - 24, 32);
            return new Rectangle(12, 56 + i * 38, SideW - 24, 34);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Prepare(g);
            g.ScaleTransform(Ui.F, Ui.F);
            Ui.Hairline(g, SideW - 1, 0, SideW - 1, (int)(ClientSize.Height / Ui.F));
            Brand.DrawMark(g, new Rectangle(22, 19, 16, 16), Ui.Text, Brand.Pixel);
            Ui.Prepare(g);
            Brand.DrawWordmark(g, 45, 22, 9.5f, Ui.Text, Brand.Pixel);
            Ui.Prepare(g);
            var footer = ItemRect(PageNames.Length - 1);
            Ui.Hairline(g, 22, footer.Y - 10, SideW - 22, footer.Y - 10);
            for (int i = 0; i < PageNames.Length; i++)
            {
                var r = ItemRect(i);
                if (i == current) Ui.Pill(g, r, Ui.Card, Ui.ControlRadius);
                else if (i == hoverItem) Ui.Pill(g, r, Ui.Side, Ui.ControlRadius);
                Icons.DrawLucide(g, PageIcons[i], r.X + 20, r.Y + r.Height / 2f, 16, 1.4f, i == current ? Ui.Text : Ui.Dim);
                using (var b = new SolidBrush(i == current ? Ui.Text : Ui.Dim))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(PageNames[i], Ui.Small, b, new RectangleF(r.X + 36, r.Y, r.Width - 40, r.Height), sf);
                    if (IsFooter(i))
                        using (var v = new SolidBrush(Color.FromArgb(110, 110, 116)))
                            g.DrawString(Updater.CurrentText, Ui.Small, v, new RectangleF(r.X + 36 + g.MeasureString(PageNames[i], Ui.Small).Width + 4, r.Y, r.Width, r.Height), sf);
                }
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
            Cursor = h >= 0 || c ? Cursors.Hand : Cursors.Default;
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
