using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Xml;

namespace Glint
{
    class CustomProvider : IUploadProvider
    {
        static readonly Regex ResponseVar = new Regex(@"\{(?:(json|xml|regex|header):([^{}]*)|(response)|(responseurl))\}", RegexOptions.IgnoreCase);
        static readonly Regex InputVar = new Regex(@"\{(filename|ext|mime|size|base64)(?::([^{}]*))?\}|\{random:([^{}]*)\}", RegexOptions.IgnoreCase);
        static readonly Random Rng = new Random();

        // Shown after a test upload, deletion links work like passwords and stay out of the log.
        public static string LastThumbnail, LastDeletion;

        public static List<KeyValuePair<string, string>> ParseList(string stored)
        {
            var list = new List<KeyValuePair<string, string>>();
            string s = (stored ?? "").Trim();
            if (s.StartsWith("["))
            {
                var rows = new JavaScriptSerializer().Deserialize<string[][]>(s);
                foreach (var r in rows) if (r.Length == 2 && r[0] != "") list.Add(new KeyValuePair<string, string>(r[0], r[1]));
                return list;
            }
            // "A: b; C: d" from older settings
            foreach (var part in s.Split(new[] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf(':');
                if (i > 0) list.Add(new KeyValuePair<string, string>(part.Substring(0, i).Trim(), part.Substring(i + 1).Trim()));
            }
            return list;
        }

        public static string StoreList(IEnumerable<KeyValuePair<string, string>> items)
        {
            var rows = items.Where(k => k.Key.Trim() != "").Select(k => new[] { k.Key.Trim(), k.Value }).ToArray();
            return rows.Length == 0 ? "" : new JavaScriptSerializer().Serialize(rows);
        }

        static string Input(string text, byte[] data, string name, string mime)
        {
            return InputVar.Replace(text ?? "", m =>
            {
                if (m.Groups[3].Success)
                {
                    var options = m.Groups[3].Value.Split('|');
                    lock (Rng) return options[Rng.Next(options.Length)];
                }
                if (m.Groups[2].Success)
                    return m.Groups[1].Value.ToLowerInvariant() == "base64" ? Convert.ToBase64String(Encoding.UTF8.GetBytes(m.Groups[2].Value)) : m.Value;
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "filename": return name;
                    case "ext": return Path.GetExtension(name).TrimStart('.');
                    case "mime": return mime;
                    case "size": return data.Length.ToString();
                    default: return Convert.ToBase64String(data);
                }
            });
        }

        static string Xml(string body, string path)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(body), settings))
            {
                var doc = new XmlDocument { XmlResolver = null };
                doc.Load(reader);
                var node = doc.SelectSingleNode(path);
                return node == null ? "" : node.InnerText;
            }
        }

        static string RegexValue(string body, string spec)
        {
            int bar = spec.LastIndexOf('|');
            string pattern = bar > 0 ? spec.Substring(0, bar) : spec, selector = bar > 0 ? spec.Substring(bar + 1) : "1";
            var m = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1)).Match(body);
            if (!m.Success) return "";
            int group;
            if (int.TryParse(selector, out group)) return m.Groups.Count > group ? m.Groups[group].Value : m.Value;
            return m.Groups[selector].Success ? m.Groups[selector].Value : "";
        }

        // Fills {json:...}, {xml:...}, {regex:...|group}, {header:...} and {response}.
        public static string FromResponse(string template, string body, WebHeaderCollection headers, string responseUrl = "")
        {
            return ResponseVar.Replace(template ?? "", m =>
            {
                if (m.Groups[3].Success) return body;
                if (m.Groups[4].Success) return responseUrl;
                string arg = m.Groups[2].Value;
                try
                {
                    switch (m.Groups[1].Value.ToLowerInvariant())
                    {
                        case "json": return HttpSend.JsonPath(body, arg.StartsWith("$.") ? arg.Substring(2) : arg) ?? "";
                        case "xml": return Xml(body, arg);
                        case "regex": return RegexValue(body, arg);
                        default: return headers == null ? "" : headers[arg] ?? "";
                    }
                }
                catch (XmlException) { return ""; }
                catch (System.Xml.XPath.XPathException) { return ""; }
                catch (ArgumentException) { return ""; }
                catch (RegexMatchTimeoutException) { return ""; }
            }).Trim();
        }

        public string Upload(Destination d, byte[] data, string name, string mime)
        {
            if (string.IsNullOrWhiteSpace(d.Url)) throw new InvalidOperationException("URL fehlt");
            string url = Input(d.Url.Trim(), data, name, mime);
            var query = ParseList(d.UrlParams).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(Input(p.Value, data, name, mime))).ToList();
            if (query.Count > 0) url += (url.Contains("?") ? "&" : "?") + string.Join("&", query);
            var headers = new Dictionary<string, string>();
            foreach (var h in ParseList(d.Headers)) headers[h.Key] = Input(h.Value, data, name, mime);
            var args = ParseList(d.BodyArgs).Select(a => new KeyValuePair<string, string>(a.Key, Input(a.Value, data, name, mime))).ToList();
            string method = string.IsNullOrWhiteSpace(d.Method) ? "POST" : d.Method.Trim().ToUpperInvariant();
            string type = string.IsNullOrWhiteSpace(d.BodyType) ? "form" : d.BodyType;

            HttpSend.Resp res;
            try
            {
                switch (type)
                {
                    case "none":
                        res = HttpSend.Send(d, method, url, null, headers, new byte[0]);
                        break;
                    case "urlencoded":
                        string form = string.Join("&", args.Select(a => Uri.EscapeDataString(a.Key) + "=" + Uri.EscapeDataString(a.Value)));
                        res = HttpSend.Send(d, method, url, "application/x-www-form-urlencoded", headers, Encoding.UTF8.GetBytes(form));
                        break;
                    case "json":
                        res = HttpSend.Send(d, method, url, "application/json", headers, Encoding.UTF8.GetBytes(Input(d.BodyText, data, name, mime)));
                        break;
                    case "xml":
                        res = HttpSend.Send(d, method, url, "application/xml", headers, Encoding.UTF8.GetBytes(Input(d.BodyText, data, name, mime)));
                        break;
                    case "binary":
                        res = HttpSend.Send(d, method, url, mime, headers, data);
                        break;
                    default:
                        string field = string.IsNullOrWhiteSpace(d.Field) ? "file" : d.Field.Trim();
                        res = HttpSend.SendMultipart(d, method, url, headers, field, data, name, mime, args);
                        break;
                }
            }
            catch (HttpError e)
            {
                string message = FromResponse(d.ErrorMsg, e.Body, e.Headers);
                if (message != "") throw new InvalidOperationException(message, e);
                throw;
            }

            LastThumbnail = FromResponse(d.ThumbUrl, res.Body, res.Headers, url);
            LastDeletion = FromResponse(d.DeleteUrl, res.Body, res.Headers, url);

            string template = d.UrlTemplate;
            if (string.IsNullOrWhiteSpace(template) && !string.IsNullOrWhiteSpace(d.JsonPath)) template = "{json:" + d.JsonPath.Trim() + "}";
            if (string.IsNullOrWhiteSpace(template)) return (HttpSend.JsonPath(res.Body, "url") ?? res.Body).Trim();
            string link = FromResponse(template, res.Body, res.Headers, url);
            if (link == "") throw new InvalidOperationException("URL-Vorlage ergab keinen Wert");
            return link;
        }
    }
}
