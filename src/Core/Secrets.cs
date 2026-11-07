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
    static class Secrets
    {
        static readonly byte[] Salt = Encoding.UTF8.GetBytes("Glint.Upload.v1");
        public static string Protect(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return "enc:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(v), Salt, DataProtectionScope.CurrentUser));
        }
        public static string Unprotect(string v)
        {
            if (string.IsNullOrEmpty(v) || !v.StartsWith("enc:")) return v ?? "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Substring(4)), Salt, DataProtectionScope.CurrentUser)); }
            catch (Exception) { return ""; }
        }
    }
}
