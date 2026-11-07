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
    class ZiplineProvider : IUploadProvider
    {
        public string Upload(Destination d, byte[] data, string name, string mime)
        {
            if (string.IsNullOrWhiteSpace(d.Url)) throw new InvalidOperationException("Zipline-URL fehlt");
            var h = Headers(d);
            return UrlFrom(HttpSend.Multipart(d, Endpoint(d.Url), h, "file", data, name, mime));
        }

        // Names as in Zipline's parseHeaders.
        public static Dictionary<string, string> Headers(Destination d)
        {
            var h = new Dictionary<string, string> { { "Authorization", d.Token } };
            Action<string, string> set = (name, value) => { if (!string.IsNullOrWhiteSpace(value)) h[name] = value.Trim(); };
            set("x-zipline-format", d.ZFormat);
            set("x-zipline-extensionless", d.ZExtensionless == "1" ? "true" : "");
            set("x-zipline-original-name", d.ZOriginalName == "1" ? "true" : "");
            string quality = (d.ZCompressQuality ?? "").Trim();
            if (quality != "")
            {
                int q;
                if (!int.TryParse(quality, out q) || q < 0 || q > 100) throw new InvalidOperationException("Kompression muss zwischen 0 und 100 liegen");
                set("x-zipline-image-compression-percent", q.ToString());
                set("x-zipline-image-compression-type", d.ZCompressType);
            }
            string views = (d.ZMaxViews ?? "").Trim();
            if (views != "")
            {
                int v;
                if (!int.TryParse(views, out v) || v < 0) throw new InvalidOperationException("Max. Aufrufe muss eine Zahl sein");
                set("x-zipline-max-views", v.ToString());
            }
            set("x-zipline-deletes-at", d.ZExpiry);
            set("x-zipline-password", d.ZPassword);
            set("x-zipline-domain", d.ZDomain);
            set("x-zipline-folder", d.ZFolder);
            return h;
        }

        // Accepts the base URL or the full upload URL.
        public static string Endpoint(string url)
        {
            string u = url.Trim().TrimEnd('/');
            if (u.EndsWith("/api/upload", StringComparison.OrdinalIgnoreCase)) u = u.Substring(0, u.Length - "/api/upload".Length);
            return u + "/api/upload";
        }

        // v3: {"files":["url"]}, v4: {"files":[{"url":"..."}]}
        public static string UrlFrom(string json)
        {
            string url = HttpSend.JsonPath(json, "files[0].url") ?? HttpSend.JsonPath(json, "files[0]");
            if (string.IsNullOrEmpty(url)) throw new InvalidOperationException("Antwort enthält keine URL");
            return url;
        }
    }
}
