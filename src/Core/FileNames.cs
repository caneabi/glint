using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Glint
{
    static class FileNames
    {
        public const string DefaultSave = "Glint {date} {time}", DefaultUpload = "{random}";
        static readonly Regex Token = new Regex(@"\{(date|time|random|counter|width|height|user|host)(?::([^{}]*))?\}", RegexOptions.IgnoreCase);
        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

        static string RandomHex(int length)
        {
            var bytes = new byte[(length + 1) / 2];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2"))).Substring(0, length);
        }

        static string DateText(DateTime now, string format, string fallback)
        {
            try { return now.ToString(string.IsNullOrEmpty(format) ? fallback : format, CultureInfo.InvariantCulture); }
            catch (FormatException) { return now.ToString(fallback, CultureInfo.InvariantCulture); }
        }

        public static string Format(string pattern, int width, int height, DateTime now, int counter)
        {
            return Token.Replace(pattern ?? "", m =>
            {
                string arg = m.Groups[2].Success ? m.Groups[2].Value : "";
                int n;
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "date": return DateText(now, arg, "yyyy-MM-dd");
                    case "time": return DateText(now, arg, "HH-mm-ss");
                    case "random": return RandomHex(int.TryParse(arg, out n) && n > 0 && n <= 64 ? n : 20);
                    case "counter": return counter.ToString().PadLeft(int.TryParse(arg, out n) && n > 0 && n <= 12 ? n : 1, '0');
                    case "width": return width.ToString();
                    case "height": return height.ToString();
                    case "user": return Environment.UserName;
                    default: return Environment.MachineName;
                }
            });
        }

        // Uploads stay URL safe, a space would also break Discord's attachment:// link.
        public static string Clean(string name, bool urlSafe)
        {
            string s = name ?? "";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            if (urlSafe) s = Regex.Replace(s, @"[^A-Za-z0-9._-]+", "-");
            s = s.Trim(' ', '.');
            if (s.Length > 120) s = s.Substring(0, 120).Trim(' ', '.');
            if (s == "") return RandomHex(20);
            if (Reserved.Contains(s.ToUpperInvariant())) s += "_";
            return s;
        }

        static int NextCounter(string pattern)
        {
            var c = Settings.Current;
            if (pattern.IndexOf("{counter", StringComparison.OrdinalIgnoreCase) < 0) return c.FileCounter;
            int value = System.Threading.Interlocked.Increment(ref c.FileCounter) - 1;
            // Runs off the UI thread during uploads, a failed save must not stop the upload.
            try { c.Save(); }
            catch (Exception ex) { Log.Error("Zähler speichern", ex); }
            return value;
        }

        public static string ForSave(int width, int height)
        {
            string p = string.IsNullOrWhiteSpace(Settings.Current.FileNameSave) ? DefaultSave : Settings.Current.FileNameSave;
            return Clean(Format(p, width, height, DateTime.Now, NextCounter(p)), false);
        }

        public static string ForUpload(string ext, int width, int height)
        {
            string p = string.IsNullOrWhiteSpace(Settings.Current.FileNameUpload) ? DefaultUpload : Settings.Current.FileNameUpload;
            return Clean(Format(p, width, height, DateTime.Now, NextCounter(p)), true) + ext;
        }

        public static string Preview(string pattern, bool urlSafe, string ext)
        {
            return Clean(Format(pattern, 1920, 1080, DateTime.Now, Settings.Current.FileCounter), urlSafe) + ext;
        }

        public static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string next = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(next)) return next;
            }
        }
    }
}
