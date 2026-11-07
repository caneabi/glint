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
    class S3Provider : IUploadProvider
    {
        static string UriEncode(string s)
        {
            var sb = new StringBuilder();
            foreach (byte b in Encoding.UTF8.GetBytes(s))
            {
                char c = (char)b;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~') sb.Append(c);
                else sb.Append('%').Append(b.ToString("X2"));
            }
            return sb.ToString();
        }

        static string Hex(byte[] b) { return string.Concat(b.Select(x => x.ToString("x2"))); }
        static byte[] Hmac(byte[] key, string msg) { using (var h = new HMACSHA256(key)) return h.ComputeHash(Encoding.UTF8.GetBytes(msg)); }
        public static string Sha256Hex(byte[] data) { using (var s = SHA256.Create()) return Hex(s.ComputeHash(data)); }

        // AWS SigV4, header names must be lowercase.
        public static string SignV4(string method, string path, SortedDictionary<string, string> headers, string payloadHash, string amzDate, string region, string accessKey, string secretKey)
        {
            string date = amzDate.Substring(0, 8), scope = date + "/" + region + "/s3/aws4_request";
            string signed = string.Join(";", headers.Keys);
            string canonical = method + "\n" + path + "\n\n" + string.Concat(headers.Select(h => h.Key + ":" + h.Value.Trim() + "\n")) + "\n" + signed + "\n" + payloadHash;
            string toSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" + Sha256Hex(Encoding.UTF8.GetBytes(canonical));
            byte[] k = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretKey), date);
            k = Hmac(k, region); k = Hmac(k, "s3"); k = Hmac(k, "aws4_request");
            return "AWS4-HMAC-SHA256 Credential=" + accessKey + "/" + scope + ",SignedHeaders=" + signed + ",Signature=" + Hex(Hmac(k, toSign));
        }

        public string Upload(Destination d, byte[] data, string name, string mime)
        {
            string endpoint = d.Type == "r2" ? "https://" + d.Account.Trim() + ".r2.cloudflarestorage.com" : d.Url.Trim().TrimEnd('/');
            string region = d.Type == "r2" ? "auto" : (string.IsNullOrWhiteSpace(d.Region) ? "us-east-1" : d.Region.Trim());
            if (d.Type == "r2" && string.IsNullOrWhiteSpace(d.Account)) throw new InvalidOperationException("Account-ID fehlt");
            if (d.Type == "s3" && string.IsNullOrWhiteSpace(d.Url)) throw new InvalidOperationException("Endpoint fehlt");
            if (string.IsNullOrWhiteSpace(d.Bucket) || string.IsNullOrWhiteSpace(d.AccessKey) || string.IsNullOrEmpty(d.SecretKey)) throw new InvalidOperationException("Bucket oder Zugangsschlüssel fehlt");
            string prefix = (d.Prefix ?? "").Trim().Trim('/');
            string key = prefix.Length > 0 ? prefix + "/" + name : name;
            string encodedKey = string.Join("/", key.Split('/').Select(UriEncode));
            string path = "/" + UriEncode(d.Bucket.Trim()) + "/" + encodedKey;
            var uri = new Uri(endpoint + path);
            string hash = Sha256Hex(data), amz = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
            var signedHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal) {
                { "content-type", mime }, { "host", uri.Authority }, { "x-amz-content-sha256", hash }, { "x-amz-date", amz } };
            var headers = new Dictionary<string, string> {
                { "x-amz-content-sha256", hash }, { "x-amz-date", amz },
                { "Authorization", SignV4("PUT", path, signedHeaders, hash, amz, region, d.AccessKey.Trim(), d.SecretKey) } };
            HttpSend.Request(d, "PUT", uri.AbsoluteUri, mime, headers, data);
            return string.IsNullOrWhiteSpace(d.PublicUrl) ? uri.AbsoluteUri : d.PublicUrl.Trim().TrimEnd('/') + "/" + encodedKey;
        }
    }
}
