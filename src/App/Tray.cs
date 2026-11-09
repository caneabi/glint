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
    class Tray : ApplicationContext
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly HotkeySink sink = new HotkeySink();
        readonly List<string> actions = new List<string>();
        Rectangle lastArea = Rectangle.Empty;
        public static Tray Instance;
        public static bool RelaunchRequested;
        public const string ShowEventName = "Local\\Glint.ShowSettings.v1", QuitEventName = "Local\\Glint.Quit.v1";
        readonly System.Threading.EventWaitHandle showSignal, quitSignal;
        bool settingsOpen;

        public void OpenSettings()
        {
            if (settingsOpen) return;
            settingsOpen = true;
            try { using (var f = new SettingsForm()) { f.Shown += delegate { f.Activate(); }; f.ShowDialog(); } }
            finally { settingsOpen = false; RegisterAll(); }
        }

        public void ApplyTrayVisibility() { icon.Visible = Settings.Current.ShowTray; }

        public void RefreshMenu()
        {
            var old = icon.ContextMenuStrip;
            icon.ContextMenuStrip = BuildMenu();
            if (old != null) old.Dispose();
        }

        public void Quit()
        {
            icon.Visible = false;
            Imaging.CleanTemp(false);
            ExitThread();
        }

        public Tray(string[] files, bool openSettings = false)
        {
            Instance = this;
            icon.Icon = Brand.TrayIcon();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += ThemeChanged;
            icon.Text = "Glint";
            icon.Visible = Settings.Current.ShowTray;
            icon.ContextMenuStrip = BuildMenu();
            icon.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) OpenSettings(); };
            KeyHook.Install();
            KeyHook.Intercept = FloatingPreview.GlobalKey;
            sink.Pressed = i => { if (i >= 0 && i < actions.Count) Run(actions[i]); };
            RegisterAll();
            foreach (var f in files) OpenPath(f);
            showSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowEventName);
            var signalTimer = new System.Windows.Forms.Timer { Interval = 300 };
            quitSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, QuitEventName);
            signalTimer.Tick += delegate { if (quitSignal.WaitOne(0)) Quit(); else if (showSignal.WaitOne(0)) OpenSettings(); };
            signalTimer.Start();
            if (openSettings || !Settings.Current.StartInTray) Later(100, OpenSettings);
            if (Settings.Current.AutoUpdate)
            {
                Later(15000, () => Updater.Check(false));
                var daily = new System.Windows.Forms.Timer { Interval = 24 * 60 * 60 * 1000 };
                daily.Tick += delegate { if (Settings.Current.AutoUpdate) Updater.Check(false); };
                daily.Start();
            }
        }

        void ThemeChanged(object s, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General) icon.Icon = Brand.TrayIcon();
        }

        public ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            MenuKit.Style(menu, true);
            var keyed = new List<KeyValuePair<ToolStripMenuItem, string>>();
            Func<string, string, ToolStripMenuItem> capture = (k, iconName) =>
            {
                var it = MenuKit.Item(L.T(Settings.Labels[k]), () => Run(k), iconName);
                keyed.Add(new KeyValuePair<ToolStripMenuItem, string>(it, k));
                return it;
            };
            var open = MenuKit.Item(L.T("Glint öffnen"), OpenSettings);
            open.Image = MenuKit.MarkImage();
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(capture("Fullscreen", "monitor"));
            menu.Items.Add(capture("Area", "square-dashed"));
            menu.Items.Add(capture("Scrolling", "chevrons-down"));
            menu.Items.Add(capture("Ocr", "scan-text"));
            menu.Items.Add(capture("Color", "droplet"));
            var more = MenuKit.Sub(L.T("Mehr"), true);
            more.Image = MenuKit.IconImage("ellipsis");
            more.DropDownItems.Add(capture("Repeat", "repeat"));
            more.DropDownItems.Add(capture("ActiveWindow", "app-window"));
            more.DropDownItems.Add(capture("Window", "mouse-pointer-2"));
            more.DropDownItems.Add(capture("Delayed", "timer"));
            more.DropDownItems.Add(capture("Upload", "cloud-upload"));
            more.DropDownItems.Add(new ToolStripSeparator());
            more.DropDownItems.Add(MenuKit.Item(L.T("Bild öffnen…"), OpenFile, "file"));
            more.DropDownItems.Add(MenuKit.Item(L.T("Aus Zwischenablage laden"), LoadClipboard, "clipboard"));
            menu.Items.Add(more);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuKit.Item("GitHub", () => System.Diagnostics.Process.Start("https://github.com/caneabi/glint"), "github"));
            menu.Items.Add(MenuKit.Item(L.T("Fehler melden"), () => System.Diagnostics.Process.Start("https://github.com/caneabi/glint/issues/new"), "bug"));
            menu.Items.Add(new ToolStripSeparator());
            var startup = MenuKit.Item(L.T("Mit Windows starten"), () =>
            {
                try { Installer.SetAutostart(!Installer.Autostart); }
                catch (Exception ex) { Toast.Error(L.T("Autostart konnte nicht geändert werden"), "Autostart", ex); }
            });
            menu.Items.Add(startup);
            menu.Items.Add(MenuKit.Item(L.T("Einstellungen…"), OpenSettings));
            menu.Items.Add(MenuKit.Item(L.T("Beenden"), Quit));
            menu.Opening += delegate
            {
                var s = Settings.Current;
                startup.Checked = Installer.Autostart;
                foreach (var kv in keyed)
                {
                    string hk;
                    kv.Key.ShortcutKeyDisplayString = s.Hotkeys.TryGetValue(kv.Value, out hk) && !string.IsNullOrEmpty(hk) ? Settings.Pretty(hk) : null;
                }
            };
            return menu;
        }

        void LoadClipboard()
        {
            if (!Clipboard.ContainsImage()) { Toast.Show(L.T("Kein Bild in der Zwischenablage")); return; }
            using (var img = Clipboard.GetImage()) new Editor(Imaging.Normalize(img)).Show();
        }

        public void RunSetupSoon() { Later(50, RunSetup); }

        void RunSetup()
        {
            SuspendHotkeys();
            try
            {
                using (var w = new SetupWizard())
                {
                    w.ShowDialog();
                    if (w.Relaunch) { RelaunchRequested = true; icon.Visible = false; ExitThread(); }
                }
            }
            finally { if (!RelaunchRequested) RegisterAll(); }
            if (!RelaunchRequested) OpenSettings();
        }

        public void SuspendHotkeys()
        {
            for (int i = 0; i < actions.Count; i++) Native.UnregisterHotKey(sink.Handle, i);
            actions.Clear();
            KeyHook.Bindings.Clear();
        }

        public void RegisterAll()
        {
            SuspendHotkeys();
            var failed = new List<string>();
            foreach (var p in Settings.Current.Hotkeys)
            {
                Keys combo;
                if (Settings.ParseKeys(p.Value, out combo) && (combo & Keys.KeyCode) == Keys.PrintScreen)
                {
                    var name = p.Key;
                    KeyHook.Bindings[combo] = () => Run(name);
                    continue;
                }
                uint mod, vk;
                if (!Settings.ParseHotkey(p.Value, out mod, out vk)) continue;
                if (Native.RegisterHotKey(sink.Handle, actions.Count, mod | 0x4000, vk)) actions.Add(p.Key);
                else { actions.Add(p.Key); failed.Add(L.T(Settings.Labels[p.Key])); }
            }
            foreach (var d in Settings.Current.Destinations)
            {
                Keys combo;
                if (!Settings.ParseKeys(d.Hotkey, out combo)) continue;
                string action = "upload:" + d.Id;
                if ((combo & Keys.KeyCode) == Keys.PrintScreen) { KeyHook.Bindings[combo] = () => Run(action); continue; }
                uint mod, vk;
                if (!Settings.ParseHotkey(d.Hotkey, out mod, out vk)) continue;
                if (!Native.RegisterHotKey(sink.Handle, actions.Count, mod | 0x4000, vk)) failed.Add("Upload " + d.Name);
                actions.Add(action);
            }
            if (failed.Count > 0) Toast.Error(L.T("Hotkey belegt"), "Hotkeys nicht registriert: " + string.Join(", ", failed), null);
        }

        // Upload the focused image, otherwise ask for an area.
        void UploadTo(Destination d)
        {
            if (d == null) { Toast.Error(L.T("Kein Upload-Ziel eingerichtet"), "Upload ohne Ziel", null); return; }
            var src = Form.ActiveForm as IImageSource;
            if (src != null)
            {
                var current = src.CurrentImage();
                Uploader.Run(current, d);
                if (src is Editor) current.Dispose(); // editors hand out a fresh export
                return;
            }
            Select(false, r2 => { Uploader.Run(r2.Item2, d); r2.Item2.Dispose(); });
        }

        public void Run(string a)
        {
            if (a.StartsWith("upload:"))
            {
                UploadTo(Settings.Current.Destinations.FirstOrDefault(x => "upload:" + x.Id == a));
                return;
            }
            switch (a)
            {
                case "Upload": UploadTo(Uploader.Active); break;
                case "Fullscreen": Deliver(Imaging.Grab(Screen.FromPoint(Cursor.Position).Bounds), false); break;
                case "Delayed":
                    Toast.Show(L.T("Aufnahme in 3 Sekunden…"), 1200);
                    Later(3000, () => Run("Fullscreen"));
                    break;
                case "ActiveWindow":
                    var r = Native.WindowBounds(Native.GetForegroundWindow());
                    if (r.Width > 0) Deliver(Imaging.GrabWindow(Native.GetForegroundWindow()), false);
                    break;
                case "Repeat":
                    if (lastArea.Width > 0) Deliver(Imaging.Grab(lastArea), true); else Run("Area");
                    break;
                case "Area": Select(false, r2 => { lastArea = r2.Item1; Deliver(r2.Item2, true); }); break;
                case "Window": Select(true, r2 => Deliver(r2.Item2, false)); break;
                case "Ocr": Select(false, r2 => OcrAndCopy(r2.Item2)); break;
                case "Color":
                    if (Overlay.Active) break;
                    var picker = new Overlay(false, true);
                    Overlay.Active = true;
                    picker.Show();
                    break;
                case "Scrolling": Select(false, r2 => { r2.Item2.Dispose(); StartScrolling(r2.Item1); }); break;
            }
        }

        public static void Select(bool windowMode, Action<Tuple<Rectangle, Bitmap>> done)
        {
            if (Overlay.Active) return;
            var o = new Overlay(windowMode);
            Overlay.Active = true;
            o.Done = done;
            o.Show();
        }

        static void Later(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        // Takes ownership of the bitmap.
        // Returns what was recognized and copied, empty when nothing was found.
        public static async Task<string> OcrCopy(Bitmap b)
        {
            string text = await Ocr.Recognize(b);
            if (string.IsNullOrWhiteSpace(text)) return "";
            Clipboard.SetText(text);
            return text;
        }

        public async void OcrAndCopy(Bitmap b)
        {
            try
            {
                string text = await OcrCopy(b);
                if (text == "") { Toast.Show(L.T("Kein Text gefunden")); return; }
                Toast.Show(L.T("Text kopiert: ") + (text.Length > 48 ? text.Substring(0, 48) + "…" : text));
            }
            catch (Exception ex) { Toast.Error(L.T("Texterkennung fehlgeschlagen"), "OCR", ex); }
            finally { b.Dispose(); }
        }

        async void StartScrolling(Rectangle r)
        {
            Toast.Show(L.T("Scrolling startet – Esc beendet"), 900);
            await Task.Delay(1100);
            var result = await ScrollCapture.Run(r, Settings.Current.MaxScroll);
            if (result != null) Deliver(result, false);
        }

        void Deliver(Bitmap b, bool area)
        {
            string afterMode = Settings.Current.After;
            if (afterMode != "copy") Sounds.Play(Sounds.Kind.Screenshot);
            string after = Settings.Current.After;
            if (after == "copy") { Imaging.Copy(b); b.Dispose(); Toast.Show(L.T("In die Zwischenablage kopiert")); return; }
            if (after == "thumbnail") { new FloatingPreview(b).Show(); return; }
            new Editor(b).Show();
        }

        public void OpenFile()
        {
            using (var d = new OpenFileDialog { Filter = "Bilder|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif" })
                if (d.ShowDialog() == DialogResult.OK) OpenPath(d.FileName);
        }

        public void OpenPath(string f)
        {
            try
            {
                new Editor(Imaging.Load(f)).Show();
            }
            catch (Exception ex) { Toast.Error(L.T("Bild konnte nicht geöffnet werden"), "Öffnen von " + f, ex); }
        }

        protected override void Dispose(bool d) { if (d) { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= ThemeChanged; icon.Dispose(); KeyHook.Uninstall(); Imaging.CleanTemp(false); } base.Dispose(d); }
    }
}
