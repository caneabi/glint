using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Glint
{
    static class Uploader
    {
        static readonly Dictionary<string, IUploadProvider> Providers = new Dictionary<string, IUploadProvider> {
            { "zipline", new ZiplineProvider() }, { "r2", new S3Provider() }, { "s3", new S3Provider() }, { "custom", new CustomProvider() }, { "discord", new DiscordProvider() } };

        static Uploader()
        {
            ServicePointManager.Expect100Continue = false;
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        }

        public static Destination Active
        {
            get
            {
                var s = Settings.Current;
                return s.Destinations.FirstOrDefault(d => d.Id == s.ActiveDestination) ?? s.Destinations.FirstOrDefault();
            }
        }

        public static void RunActive(Bitmap b)
        {
            var d = Active;
            if (d == null) { Toast.Error(L.T("Kein Upload-Ziel eingerichtet"), "Upload ohne Ziel", null); return; }
            Run(b, d);
        }

        // Uploads a copy and puts the link on the clipboard. False: uploaded, but the clipboard was locked.
        public static async Task<bool> Upload(Bitmap b, Destination d)
        {
            var copy = Imaging.Crop(b, new Rectangle(0, 0, b.Width, b.Height));
            try
            {
                string url = await Task.Run(() =>
                {
                    string ext, mime;
                    var data = Encode(copy, out ext, out mime);
                    return Send(d, data, ext, mime, copy.Width, copy.Height);
                });
                Log.Info("Upload nach " + d.Name + ": " + url);
                Sounds.Play(Sounds.Kind.Upload);
                try { Clipboard.SetText(url); return true; }
                catch (System.Runtime.InteropServices.ExternalException ex) { Log.Error("Zwischenablage", ex); return false; }
            }
            finally { copy.Dispose(); }
        }

        public static async void Run(Bitmap b, Destination d)
        {
            Toast.Show(L.T("Lädt hoch…"), 15000);
            try
            {
                if (await Upload(b, d)) Toast.Show(L.T("Link kopiert"), 2000);
                else Toast.Error(L.T("Hochgeladen, Link steht im Log"), "Zwischenablage", null);
            }
            catch (Exception ex) { Toast.Error(L.T("Upload fehlgeschlagen"), "Upload nach " + d.Name + " (" + d.TypeName + ")", ex); }
        }

        public static byte[] Encode(Bitmap b, out string ext, out string mime)
        {
            var f = Settings.Current.Format;
            if (f == "auto") f = Imaging.LooksPhotographic(b) ? "jpg" : "png";
            ext = f == "jpg" ? ".jpg" : f == "webp" ? ".webp" : ".png";
            mime = f == "jpg" ? "image/jpeg" : f == "webp" ? "image/webp" : "image/png";
            string temp = Imaging.TempFile(ext);
            try { Imaging.SaveFile(b, temp); return File.ReadAllBytes(temp); }
            finally { try { File.Delete(temp); } catch (IOException) { } }
        }

        public static string FileName(string ext) { return FileNames.ForUpload(ext, 0, 0); }

        public static string Send(Destination d, byte[] data, string ext, string mime, int width = 0, int height = 0)
        {
            IUploadProvider p;
            if (!Providers.TryGetValue(d.Type, out p)) throw new InvalidOperationException("Unbekannter Zieltyp: " + d.Type);
            string url = p.Upload(d, data, FileNames.ForUpload(ext, width, height), mime);
            Uri parsed;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Server lieferte keinen gültigen Link: " + (url != null && url.Length > 200 ? url.Substring(0, 200) : url));
            return url.Trim();
        }

        public static void ShowMenu(Action<Destination> pick)
        {
            var menu = new ContextMenuStrip();
            MenuKit.Style(menu, true);
            var active = Active;
            foreach (var d in Settings.Current.Destinations)
            {
                var dest = d;
                var item = MenuKit.Item(d.Name, () => pick(dest));
                item.Checked = d == active;
                menu.Items.Add(item);
            }
            if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem(L.T("Kein Ziel eingerichtet")) { Enabled = false });
            menu.Closed += delegate { menu.BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
    }
}
