using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Glint
{
    // Reads ShareX custom uploader files (.sxcu).
    static class ShareXImport
    {
        const long MaxFile = 1024 * 1024;
        static readonly Regex Interactive = new Regex(@"\{(select|inputbox|outputbox|input)(:[^{}]*)?\}", RegexOptions.IgnoreCase);
        static readonly Regex OldSyntax = new Regex(@"\$((?:json|xml|regex|header|response|responseurl|filename|random|base64)(?::[^$]*)?)\$", RegexOptions.IgnoreCase);

        public class Result { public Destination Target; public List<string> Unsupported = new List<string>(); public bool ImageCapable = true; }

        static string Text(Dictionary<string, object> o, string key)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        static string Convert(string s) { return OldSyntax.Replace(s ?? "", m => "{" + m.Groups[1].Value + "}"); }

        static string List(Dictionary<string, object> o, string key, Result r)
        {
            object v;
            var map = o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
            if (map == null) return "";
            return CustomProvider.StoreList(map.Select(kv => new KeyValuePair<string, string>(kv.Key, Note(Convert(kv.Value == null ? "" : kv.Value.ToString()), r))));
        }

        static string Note(string s, Result r)
        {
            foreach (Match m in Interactive.Matches(s ?? ""))
                if (!r.Unsupported.Contains(m.Value)) r.Unsupported.Add(m.Value);
            return s;
        }

        public static Result Parse(string json)
        {
            var o = new JavaScriptSerializer { MaxJsonLength = (int)MaxFile * 2 }.DeserializeObject(json) as Dictionary<string, object>;
            if (o == null) throw new InvalidOperationException("Keine ShareX-Datei");
            string url = Text(o, "RequestURL");
            if (url == "") url = Text(o, "RequestUrl");
            if (url == "") throw new InvalidOperationException("ShareX-Datei ohne RequestURL");

            var r = new Result();
            var kinds = Text(o, "DestinationType");
            r.ImageCapable = kinds == "" || kinds.IndexOf("ImageUploader", StringComparison.OrdinalIgnoreCase) >= 0 || kinds.IndexOf("FileUploader", StringComparison.OrdinalIgnoreCase) >= 0;

            var d = new Destination { Type = "custom" };
            d.Url = Note(Convert(url), r);
            d.Name = Text(o, "Name");
            if (d.Name == "")
            {
                Uri u;
                d.Name = Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : "ShareX";
            }
            d.Method = Text(o, "RequestMethod");
            if (d.Method == "") d.Method = Text(o, "RequestType");
            if (d.Method == "") d.Method = "POST";
            d.Method = d.Method.ToUpperInvariant();
            d.UrlParams = List(o, "Parameters", r);
            d.Headers = List(o, "Headers", r);
            d.BodyArgs = List(o, "Arguments", r);
            switch (Text(o, "Body").ToLowerInvariant())
            {
                case "none": d.BodyType = "none"; break;
                case "formurlencoded": d.BodyType = "urlencoded"; break;
                case "json": d.BodyType = "json"; break;
                case "xml": d.BodyType = "xml"; break;
                case "binary": d.BodyType = "binary"; break;
                default: d.BodyType = "form"; break;
            }
            d.BodyText = Note(Convert(Text(o, "Data")), r);
            d.Field = Text(o, "FileFormName");
            d.UrlTemplate = Note(Convert(Text(o, "URL")), r);
            d.ThumbUrl = Note(Convert(Text(o, "ThumbnailURL")), r);
            d.DeleteUrl = Note(Convert(Text(o, "DeletionURL")), r);
            d.ErrorMsg = Note(Convert(Text(o, "ErrorMessage")), r);
            r.Target = d;
            return r;
        }

        public static Result Load(string path)
        {
            if (new FileInfo(path).Length > MaxFile) throw new InvalidOperationException("ShareX-Datei ist zu groß");
            return Parse(File.ReadAllText(path));
        }
    }
}
