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
    class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glint");
        public static string FileOverride;
        static string FilePath { get { return FileOverride ?? Path.Combine(Dir, "settings.ini"); } }
        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string> {
            {"Fullscreen","Vollbild"},{"Area","Bereich"},{"Window","Fenster wählen"},{"ActiveWindow","Aktives Fenster"},
            {"Ocr","Text erkennen (OCR)"},{"Scrolling","Scrolling-Aufnahme"},{"Repeat","Bereich wiederholen"},{"Delayed","Vollbild in 3 s"},{"Upload","Bereich hochladen"}};
        public Dictionary<string, string> Hotkeys = new Dictionary<string, string> {
            {"Fullscreen","Ctrl+Shift+D1"},{"Area","PrintScreen"},{"Window","Ctrl+Shift+D3"},{"ActiveWindow","Ctrl+Shift+D4"},
            {"Ocr","Ctrl+Shift+D5"},{"Scrolling","Ctrl+Shift+D6"},{"Repeat","Ctrl+Shift+D7"},{"Delayed","Ctrl+Shift+D8"},{"Upload",""}};
        public string SaveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Glint");
        public string After = "thumbnail"; // editor | copy | thumbnail
        public bool Snap = true, Freeze = true, Crosshair = false;
        public int JpegQuality = 92, OutputScale = 100;
        public List<Destination> Destinations = new List<Destination>();
        public string ActiveDestination = "";
        public bool SetupDone, ShowTray = true, StartInTray = true, AutoUpdate = true, GlobalKeys = true, PreviewOnMain, SoundScreenshot = true, SoundUpload = true, SoundCopy = true;
        public string Language = "auto", UpdateChannel = "stable", WindowBg = "trim", WindowBgColor = "#404448", FileNameSave = FileNames.DefaultSave, FileNameUpload = FileNames.DefaultUpload;
        public int FileCounter = 1;
        public static readonly Dictionary<string, string> PreviewLabels = new Dictionary<string, string> {
            {"copy","Schwebendes Fenster: Kopieren"},{"edit","Schwebendes Fenster: Editor"},{"pin","Schwebendes Fenster: Anpinnen"},
            {"save","Schwebendes Fenster: Speichern"},{"upload","Schwebendes Fenster: Hochladen"},{"ocr","Schwebendes Fenster: Text"},{"close","Schwebendes Fenster: Schließen"}};
        public Dictionary<string, string> PreviewKeys = new Dictionary<string, string> {
            {"copy","Escape"},{"edit","Return"},{"pin","Ctrl+P"},{"save","Ctrl+S"},{"upload","Ctrl+U"},{"ocr","Ctrl+T"},{"close","Delete"}};
        public string Format = "auto"; // auto | png | jpg | webp
        public string ColorFormat = "hex"; // hex | rgb | oklch
        public int MaxScroll = 20000;
        public static Settings Current = new Settings();

        static readonly object SaveGate = new object();
        // Secrets DPAPI can't read right now, kept so saving doesn't erase them.
        static readonly Dictionary<string, string> Unreadable = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                bool v2 = false, v3 = false, sawLanguage = false;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i < 1) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    if (k == "version") { v2 = v == "2" || v == "3"; v3 = v == "3"; }
                    else if (k == "uploadActive") Current.ActiveDestination = v;
                    else if (k == "setupDone") Current.SetupDone = v == "1";
                    else if (k == "language") { Current.Language = v; sawLanguage = true; }
                    else if (k == "showTray") Current.ShowTray = v == "1";
                    else if (k == "startInTray") Current.StartInTray = v == "1";
                    else if (k == "autoUpdate") Current.AutoUpdate = v == "1";
                    else if (k == "globalKeys") Current.GlobalKeys = v == "1";
                    else if (k == "previewOnMain") Current.PreviewOnMain = v == "1";
                    else if (k == "windowBg") Current.WindowBg = v;
                    else if (k == "fileNameSave") Current.FileNameSave = v;
                    else if (k == "fileNameUpload") Current.FileNameUpload = v;
                    else if (k == "fileCounter") int.TryParse(v, out Current.FileCounter);
                    else if (k == "windowBgColor") Current.WindowBgColor = v;
                    else if (k == "soundScreenshot") Current.SoundScreenshot = v == "1";
                    else if (k == "soundUpload") Current.SoundUpload = v == "1";
                    else if (k == "soundCopy") Current.SoundCopy = v == "1";
                    else if (k == "updateChannel") Current.UpdateChannel = v == "nightly" ? "nightly" : "stable";
                    else if (k.StartsWith("dest."))
                    {
                        var parts = k.Split('.');
                        if (parts.Length != 3) continue;
                        var d = Current.Destinations.FirstOrDefault(x => x.Id == parts[1]);
                        if (d == null) { d = new Destination { Id = parts[1] }; Current.Destinations.Add(d); }
                        bool secret = Array.IndexOf(Destination.SecretFields, parts[2]) >= 0;
                        string plain = secret ? Secrets.Unprotect(v) : v;
                        if (secret && plain == "" && v != "") { Unreadable[k] = v; Log.Info("Secret nicht lesbar: " + k); }
                        d.Set(parts[2], plain);
                    }
                    else if (k.StartsWith("hotkey.")) Current.Hotkeys[k.Substring(7)] = v;
                    else if (k.StartsWith("preview.")) Current.PreviewKeys[k.Substring(8)] = v;
                    else if (k == "snap") Current.Snap = v == "1";
                    else if (k == "freeze") Current.Freeze = v == "1";
                    else if (k == "crosshair") Current.Crosshair = v == "1";
                    else if (k == "jpegQuality") int.TryParse(v, out Current.JpegQuality);
                    else if (k == "outputScale") int.TryParse(v, out Current.OutputScale);
                    else if (k == "saveDir") Current.SaveDir = v;
                    else if (k == "after") Current.After = v;
                    else if (k == "format") Current.Format = v;
                    else if (k == "colorFormat") Current.ColorFormat = v;
                    else if (k == "maxScroll") int.TryParse(v, out Current.MaxScroll);
                }
                if (!v2 && Current.Hotkeys["Area"] == "Ctrl+Shift+D2") Current.Hotkeys["Area"] = "PrintScreen";
                if (!v3)
                    foreach (var old in new[] { "pin|P", "save|S", "upload|U", "ocr|T" })
                    {
                        var parts2 = old.Split('|');
                        string cur;
                        if (Current.PreviewKeys.TryGetValue(parts2[0], out cur) && cur == parts2[1]) Current.PreviewKeys[parts2[0]] = "Ctrl+" + parts2[1];
                    }
                if (!sawLanguage) Current.Language = "de"; // existing installs stay German
            }
            catch (Exception ex) { Log.Error("Einstellungen laden", ex); }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var sb = new StringBuilder();
            sb.AppendLine("version=3");
            sb.AppendLine("uploadActive=" + ActiveDestination);
            sb.AppendLine("setupDone=" + (SetupDone ? "1" : "0"));
            sb.AppendLine("language=" + Language).AppendLine("showTray=" + (ShowTray ? "1" : "0")).AppendLine("startInTray=" + (StartInTray ? "1" : "0")).AppendLine("autoUpdate=" + (AutoUpdate ? "1" : "0")).AppendLine("globalKeys=" + (GlobalKeys ? "1" : "0")).AppendLine("previewOnMain=" + (PreviewOnMain ? "1" : "0")).AppendLine("windowBg=" + WindowBg).AppendLine("windowBgColor=" + WindowBgColor).AppendLine("fileNameSave=" + FileNameSave).AppendLine("fileNameUpload=" + FileNameUpload).AppendLine("fileCounter=" + FileCounter).AppendLine("soundScreenshot=" + (SoundScreenshot ? "1" : "0")).AppendLine("soundUpload=" + (SoundUpload ? "1" : "0")).AppendLine("soundCopy=" + (SoundCopy ? "1" : "0")).AppendLine("updateChannel=" + UpdateChannel);
            foreach (var d in Destinations)
                foreach (var f in Destination.Fields)
                {
                    string v = d.Get(f);
                    string raw;
                    if (string.IsNullOrEmpty(v) && Unreadable.TryGetValue("dest." + d.Id + "." + f, out raw)) { sb.AppendLine("dest." + d.Id + "." + f + "=" + raw); continue; }
                    if (string.IsNullOrEmpty(v)) continue;
                    sb.AppendLine("dest." + d.Id + "." + f + "=" + (Array.IndexOf(Destination.SecretFields, f) >= 0 ? Secrets.Protect(v) : v.Replace("\r", "").Replace("\n", " ")));
                }
            foreach (var p in Hotkeys) sb.AppendLine("hotkey." + p.Key + "=" + p.Value);
            foreach (var p in PreviewKeys) sb.AppendLine("preview." + p.Key + "=" + p.Value);
            sb.AppendLine("snap=" + (Snap ? "1" : "0")).AppendLine("freeze=" + (Freeze ? "1" : "0")).AppendLine("crosshair=" + (Crosshair ? "1" : "0"));
            sb.AppendLine("saveDir=" + SaveDir).AppendLine("after=" + After).AppendLine("format=" + Format)
              .AppendLine("colorFormat=" + ColorFormat).AppendLine("maxScroll=" + MaxScroll).AppendLine("jpegQuality=" + JpegQuality).AppendLine("outputScale=" + OutputScale);
            // Written next to the file first, a crash can't leave half a settings file.
            lock (SaveGate)
            {
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, sb.ToString());
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
        }

        public static string KeyName(Keys k)
        {
            var parts = new List<string>();
            if ((k & Keys.Control) != 0) parts.Add("Ctrl");
            if ((k & Keys.Shift) != 0) parts.Add("Shift");
            if ((k & Keys.Alt) != 0) parts.Add("Alt");
            var code = k & Keys.KeyCode;
            parts.Add(code == Keys.PrintScreen ? "PrintScreen" : code == Keys.Return ? "Return" : code.ToString());
            return string.Join("+", parts);
        }

        public static string Pretty(string s)
        {
            if (string.IsNullOrEmpty(s)) return L.T("Nicht belegt");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bD(\d)\b", "$1");
            s = s.Replace("Return", "Enter").Replace("Escape", "Esc");
            if (L.En) return s.Replace("PrintScreen", "PrtSc").Replace("Snapshot", "PrtSc").Replace("Delete", "Del");
            return s.Replace("Ctrl", "Strg").Replace("Shift", "Umschalt").Replace("PrintScreen", "Druck").Replace("Snapshot", "Druck").Replace("Delete", "Entf");
        }

        public static bool ParseKeys(string s, out Keys result)
        {
            result = Keys.None;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var part in s.Split('+'))
            {
                if (part == "Ctrl") result |= Keys.Control;
                else if (part == "Shift") result |= Keys.Shift;
                else if (part == "Alt") result |= Keys.Alt;
                else { Keys k; if (!Enum.TryParse(part, out k)) return false; result |= k; }
            }
            return true;
        }

        public static bool ParseHotkey(string s, out uint mod, out uint vk)
        {
            mod = 0; vk = 0;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var part in s.Split('+'))
            {
                if (part == "Ctrl") mod |= 2;
                else if (part == "Shift") mod |= 4;
                else if (part == "Alt") mod |= 1;
                else if (part == "Win") mod |= 8;
                else { Keys k; if (!Enum.TryParse(part, out k)) return false; vk = (uint)k; }
            }
            return vk != 0;
        }
    }
}
