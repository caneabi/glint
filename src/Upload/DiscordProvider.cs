using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Glint
{
    // The webhook URL works like a password, so it's stored in the encrypted token field.
    class DiscordProvider : IUploadProvider
    {
        public const string DefaultColor = "#FD534C", DefaultFooter = "Glint · github.com/caneabi/glint",
            DefaultFooterIcon = "https://raw.githubusercontent.com/caneabi/glint/main/assets/glint-256.png";
        const int TitleMax = 256, DescriptionMax = 4096, FooterMax = 2048;
        static readonly Regex Webhook = new Regex(@"^/api(/v\d+)?/webhooks/\d+/[\w-]+/?$");
        static readonly Regex Created = new Regex(@"\{created(:[tTdDfFR])?\}", RegexOptions.IgnoreCase);

        public static Uri ParseWebhook(string url)
        {
            Uri u;
            if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out u)) throw new InvalidOperationException("Webhook-URL fehlt oder ist ungültig");
            bool official = u.Scheme == Uri.UriSchemeHttps && Regex.IsMatch(u.Host, @"^((ptb|canary)\.)?discord(app)?\.com$", RegexOptions.IgnoreCase);
            if ((!official && !u.IsLoopback) || !Webhook.IsMatch(u.AbsolutePath)) throw new InvalidOperationException("Das ist keine Discord-Webhook-URL");
            return u;
        }

        public static int ParseColor(string hex)
        {
            int c;
            string s = (hex ?? "").Trim().TrimStart('#');
            if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c)) return c;
            return int.Parse(DefaultColor.Substring(1), NumberStyles.HexNumber);
        }

        public static string FormatSize(long bytes)
        {
            var inv = CultureInfo.InvariantCulture;
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", inv) + " KB";
            return (bytes / 1048576.0).ToString("0.##", inv) + " MB";
        }

        static string Fill(string text, string name, string mime, long size)
        {
            return Regex.Replace(text, @"\{(filename|ext|mime|size)\}", m =>
            {
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "filename": return name;
                    case "ext": return Path.GetExtension(name).TrimStart('.');
                    case "mime": return mime;
                    default: return FormatSize(size);
                }
            }, RegexOptions.IgnoreCase);
        }

        static string Limit(string s, int max) { return s.Length > max ? s.Substring(0, max) : s; }

        public static string Payload(Destination d, string name, string mime, long size, DateTimeOffset now)
        {
            long unix = now.ToUnixTimeSeconds();
            var embed = new Dictionary<string, object> {
                { "color", ParseColor(d.Color) },
                { "image", new Dictionary<string, object> { { "url", "attachment://" + name } } } };

            // <t:...> only renders in the description.
            string description = Created.Replace(Fill(d.Description ?? "", name, mime, size), m => "<t:" + unix + (m.Groups[1].Success ? m.Groups[1].Value : ":f") + ">").Trim();
            if (description != "") embed["description"] = Limit(description, DescriptionMax);

            string title = Created.Replace(Fill(d.Title ?? "", name, mime, size), now.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Trim();
            if (title != "") embed["title"] = Limit(title, TitleMax);

            // Footers don't render markdown, Discord's own timestamp field shows next to them.
            string rawFooter = Fill(d.Footer ?? "", name, mime, size);
            string footerText = Created.Replace(rawFooter, "").Trim();
            if (Created.IsMatch(rawFooter)) embed["timestamp"] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            if (footerText != "")
            {
                var footer = new Dictionary<string, object> { { "text", Limit(footerText, FooterMax) } };
                Uri icon;
                if (Uri.TryCreate((d.FooterIcon ?? "").Trim(), UriKind.Absolute, out icon) && icon.Scheme == Uri.UriSchemeHttps) footer["icon_url"] = icon.AbsoluteUri;
                embed["footer"] = footer;
            }

            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "embeds", new object[] { embed } },
                { "allowed_mentions", new Dictionary<string, object> { { "parse", new object[0] } } } });
        }

        public string Upload(Destination d, byte[] data, string name, string mime)
        {
            var uri = ParseWebhook(d.Token);
            string url = uri.GetLeftPart(UriPartial.Path) + (uri.Query.Length > 1 ? uri.Query + "&" : "?") + "wait=true";
            var fields = new Dictionary<string, string> { { "payload_json", Payload(d, name, mime, data.Length, DateTimeOffset.Now) } };
            string body = HttpSend.Multipart(d, url, new Dictionary<string, string>(), "files[0]", data, name, mime, fields);
            string link = HttpSend.JsonPath(body, "attachments[0].url") ?? HttpSend.JsonPath(body, "embeds[0].image.url");
            if (string.IsNullOrEmpty(link)) throw new InvalidOperationException("Antwort enthält keine URL");
            return link;
        }
    }
}
