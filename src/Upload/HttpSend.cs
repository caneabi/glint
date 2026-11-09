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
    class HttpError : InvalidOperationException
    {
        public readonly string Body;
        public readonly WebHeaderCollection Headers;
        public HttpError(string message, string body, WebHeaderCollection headers) : base(message) { Body = body; Headers = headers; }
    }

    static class HttpSend
    {
        public class Resp { public string Body; public WebHeaderCollection Headers; }

        const int MaxResponse = 1024 * 1024;

        // Set by the uploader on its worker thread, aborting the request cancels the upload.
        [ThreadStatic] public static System.Threading.CancellationToken Cancel;

        static void CheckTransport(Destination d, Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            if (uri.Scheme != Uri.UriSchemeHttp) throw new InvalidOperationException("Nur http und https sind erlaubt");
            if (uri.IsLoopback || d.AllowHttp == "1") return;
            throw new InvalidOperationException("Unverschlüsseltes HTTP ist für dieses Ziel nicht freigegeben");
        }

        // Quotes and line breaks can't appear inside a multipart name.
        static string Quote(string s) { return (s ?? "").Replace("\r", "").Replace("\n", "").Replace("\"", "%22"); }

        public static string Multipart(Destination d, string url, Dictionary<string, string> headers, string field, byte[] data, string name, string mime, Dictionary<string, string> fields = null)
        {
            return SendMultipart(d, "POST", url, headers, field, data, name, mime, fields).Body;
        }

        public static Resp SendMultipart(Destination d, string method, string url, Dictionary<string, string> headers, string field, byte[] data, string name, string mime, IEnumerable<KeyValuePair<string, string>> fields)
        {
            string boundary = "----Glint" + Guid.NewGuid().ToString("N");
            string extra = "";
            if (fields != null)
                foreach (var f in fields)
                    extra += "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + Quote(f.Key) + "\"\r\n\r\n" + f.Value + "\r\n";
            byte[] head = Encoding.UTF8.GetBytes(extra + "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + Quote(field) + "\"; filename=\"" + Quote(name) + "\"\r\nContent-Type: " + mime + "\r\n\r\n");
            byte[] tail = Encoding.ASCII.GetBytes("\r\n--" + boundary + "--\r\n");
            var body = new byte[head.Length + data.Length + tail.Length];
            Buffer.BlockCopy(head, 0, body, 0, head.Length);
            Buffer.BlockCopy(data, 0, body, head.Length, data.Length);
            Buffer.BlockCopy(tail, 0, body, head.Length + data.Length, tail.Length);
            return Send(d, method, url, "multipart/form-data; boundary=" + boundary, headers, body);
        }

        static string ReadLimited(WebResponse res)
        {
            using (var s = res.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > MaxResponse) throw new InvalidOperationException("Serverantwort ist zu groß");
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static string Request(Destination d, string method, string url, string contentType, Dictionary<string, string> headers, byte[] body)
        {
            return Send(d, method, url, contentType, headers, body).Body;
        }

        // Some headers can only be set through properties on HttpWebRequest.
        static void SetHeader(HttpWebRequest req, string name, string value)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "content-type": req.ContentType = value; break;
                case "user-agent": req.UserAgent = value; break;
                case "accept": req.Accept = value; break;
                case "referer": req.Referer = value; break;
                case "host": req.Host = value; break;
                case "expect": case "connection": case "content-length": case "date": case "if-modified-since": case "range": case "transfer-encoding": break;
                default: req.Headers[name.Trim()] = value; break;
            }
        }

        public static Resp Send(Destination d, string method, string url, string contentType, Dictionary<string, string> headers, byte[] body)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) throw new InvalidOperationException("Ungültige URL");
            CheckTransport(d, uri);
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = method;
            if (contentType != null) req.ContentType = contentType;
            req.UserAgent = "Glint";
            req.Timeout = 120000;
            req.AllowAutoRedirect = false; // don't send credentials to another host
            foreach (var h in headers)
                if (!string.IsNullOrEmpty(h.Value)) SetHeader(req, h.Key, h.Value);
            var cancel = Cancel;
            cancel.ThrowIfCancellationRequested();
            using (cancel.Register(req.Abort))
            try
            {
                if (body.Length > 0)
                {
                    req.ContentLength = body.Length;
                    using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                }
                else if (method != "GET" && method != "HEAD") req.ContentLength = 0;
                using (var res = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)res.StatusCode;
                    if (code >= 300) throw new InvalidOperationException("Weiterleitung nicht erlaubt: HTTP " + code + " nach " + res.Headers["Location"]);
                    return new Resp { Body = ReadLimited(res), Headers = res.Headers };
                }
            }
            catch (WebException ex)
            {
                if (cancel.IsCancellationRequested) throw new OperationCanceledException(cancel);
                var res = ex.Response as HttpWebResponse;
                if (res == null) throw new InvalidOperationException(ex.Status + ": " + ex.Message, ex);
                string text = ReadLimited(res);
                throw new HttpError("HTTP " + (int)res.StatusCode + " " + res.StatusDescription + ": " + (text.Length > 300 ? text.Substring(0, 300) : text), text, res.Headers);
            }
        }

        public static string JsonPath(string json, string path)
        {
            object cur;
            try { cur = new JavaScriptSerializer { MaxJsonLength = MaxResponse * 2 }.DeserializeObject(json); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            foreach (Match m in Regex.Matches(path, @"([^.\[\]]+)|\[(\d+)\]"))
            {
                if (m.Groups[2].Success)
                {
                    var arr = cur as object[];
                    int i = int.Parse(m.Groups[2].Value);
                    if (arr == null || i >= arr.Length) return null;
                    cur = arr[i];
                }
                else
                {
                    var o = cur as Dictionary<string, object>;
                    if (o == null || !o.TryGetValue(m.Groups[1].Value, out cur)) return null;
                }
            }
            return cur == null || cur is Dictionary<string, object> || cur is object[] ? null : cur.ToString();
        }
    }
}
