using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Glint
{
    // Only links to the download, never installs anything.
    static class Updater
    {
        const string Api = "https://api.github.com/repos/caneabi/glint/releases/";
        public static string FeedFor(string channel) { return Api + (channel == "nightly" ? "tags/nightly" : "latest"); }

        public static Version Current { get { return Assembly.GetEntryAssembly().GetName().Version; } }

        public static string CurrentCommit
        {
            get
            {
                var a = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                int i = a == null ? -1 : a.InformationalVersion.IndexOf('+');
                if (i < 0) return "";
                string sha = a.InformationalVersion.Substring(i + 1);
                return sha.Length > 7 ? sha.Substring(0, 7) : sha;
            }
        }

        public static string CurrentText
        {
            get
            {
                var v = Current;
                string text = v.Major + "." + v.Minor + "." + v.Build;
                return Settings.Current.UpdateChannel == "nightly" && CurrentCommit != "" ? text + " · " + CurrentCommit : text;
            }
        }

        public class Info { public string Label, Url; public bool Newer; }

        static string SafeLink(string json)
        {
            Uri u;
            if (!Uri.TryCreate(HttpSend.JsonPath(json, "html_url") ?? "", UriKind.Absolute, out u) || u.Scheme != Uri.UriSchemeHttps || u.Host != "github.com")
                throw new InvalidOperationException("Invalid release link");
            return u.AbsoluteUri;
        }

        public static Info ParseStable(string json, Version current)
        {
            string tag = (HttpSend.JsonPath(json, "tag_name") ?? "").TrimStart('v');
            Version v;
            if (!Version.TryParse(tag, out v)) throw new InvalidOperationException("Invalid release tag");
            return new Info { Label = tag, Url = SafeLink(json), Newer = Normalize(v) > Normalize(current) };
        }

        // Title is "Nightly <commit>", any other commit counts as an update.
        public static Info ParseNightly(string json, string currentCommit)
        {
            string name = (HttpSend.JsonPath(json, "name") ?? "").Trim();
            string commit = name.StartsWith("Nightly ") ? name.Substring(8).Trim() : "";
            if (commit == "") throw new InvalidOperationException("Invalid nightly title");
            return new Info { Label = name, Url = SafeLink(json), Newer = commit != currentCommit };
        }

        static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }

        static Info Fetch(string channel)
        {
            var req = (HttpWebRequest)WebRequest.Create(FeedFor(channel));
            req.UserAgent = "Glint"; req.Accept = "application/vnd.github+json"; req.Timeout = 15000; req.AllowAutoRedirect = false;
            try
            {
                using (var res = req.GetResponse())
                using (var s = res.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[8192]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        ms.Write(buf, 0, n);
                        if (ms.Length > 1024 * 1024) throw new InvalidOperationException("Release response too large");
                    }
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    return channel == "nightly" ? ParseNightly(json, CurrentCommit) : ParseStable(json, Current);
                }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) return null; // nothing released yet
                throw;
            }
        }

        public static async void Check(bool manual)
        {
            string channel = Settings.Current.UpdateChannel;
            try
            {
                var info = await Task.Run(() => Fetch(channel));
                if (info != null && info.Newer)
                {
                    string url = info.Url;
                    Toast.Action(string.Format(L.T("Update auf {0} verfügbar, zum Herunterladen klicken"), info.Label), 10000, () => Process.Start(url));
                }
                else if (manual) Toast.Show(L.T("Auf dem neuesten Stand"), 2000);
            }
            catch (Exception ex)
            {
                if (manual) Toast.Error(L.T("Update-Prüfung fehlgeschlagen"), "Update check", ex);
                else Log.Error("Update check", ex);
            }
        }
    }
}
