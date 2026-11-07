using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glint
{
    static class Log
    {
        public static string Folder = Path.Combine(Settings.Dir, "logs");
        const long MaxSize = 1024 * 1024;
        static readonly object Gate = new object();
        public static string FilePath { get { return Path.Combine(Folder, "glint.log"); } }

        public static string Scrub(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = Regex.Replace(s, @"(Authorization|Credential|Signature|token|secret\w*)(\s*[:=]\s*)\S+", "$1$2***", RegexOptions.IgnoreCase);
            foreach (var d in Settings.Current.Destinations.ToArray().Concat(Masked.ToArray()))
                foreach (var v in SecretsOf(d))
                    if (!string.IsNullOrEmpty(v) && v.Length >= 4) s = s.Replace(v, "***");
            return s;
        }

        // Unsaved targets from a test upload.
        static readonly List<Destination> Masked = new List<Destination>();
        public static void Mask(Destination d) { Masked.Add(d); }

        static IEnumerable<string> SecretsOf(Destination d)
        {
            var values = new List<string> { d.Token, d.SecretKey, d.AccessKey, d.ZPassword };
            foreach (var list in new[] { d.Headers, d.UrlParams, d.BodyArgs })
            {
                try { values.AddRange(CustomProvider.ParseList(list).Select(p => p.Value)); }
                catch (Exception) { } // unreadable list, nothing to mask
            }
            return values;
        }

        static void Write(string level, string text)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    var f = new FileInfo(FilePath);
                    if (f.Exists && f.Length > MaxSize)
                    {
                        string old = FilePath + ".1";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(FilePath, old);
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + Scrub(text) + Environment.NewLine);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Info(string text) { Write("INFO ", text); }
        public static void Error(string context, Exception ex) { Write("ERROR", context + (ex == null ? "" : ": " + ex)); }

        public static void Open()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (!File.Exists(FilePath)) File.AppendAllText(FilePath, "");
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + FilePath + "\"") { UseShellExecute = false });
            }
            catch (Exception ex) { Error("Log öffnen", ex); }
        }
    }
}
