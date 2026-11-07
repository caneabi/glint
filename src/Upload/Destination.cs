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
    class Destination
    {
        public static readonly string[] SecretFields = { "url", "token", "accessKey", "secretKey", "headers", "urlParams", "bodyArgs", "bodyText", "zPassword" };
        public string Id = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Type = "zipline"; // zipline | r2 | s3 | discord | custom
        public string Name = "", Url = "", Token = "", Account = "", AccessKey = "", SecretKey = "", Bucket = "", Region = "", PublicUrl = "", Prefix = "", Field = "file", Headers = "", JsonPath = "", Hotkey = "", AllowHttp = "", Title = "", Description = "", Color = "", Footer = "", FooterIcon = "", Method = "", UrlParams = "", BodyType = "", BodyArgs = "", BodyText = "", UrlTemplate = "", ThumbUrl = "", DeleteUrl = "", ErrorMsg = "", ZFormat = "", ZExtensionless = "", ZOriginalName = "", ZCompressType = "", ZCompressQuality = "", ZMaxViews = "", ZExpiry = "", ZPassword = "", ZDomain = "", ZFolder = "";

        public string TypeName
        {
            get { return Type == "zipline" ? "Zipline" : Type == "r2" ? "Cloudflare R2" : Type == "s3" ? "S3" : Type == "discord" ? "Discord" : L.T("Eigene"); }
        }

        public string Get(string f)
        {
            switch (f)
            {
                case "type": return Type; case "name": return Name; case "url": return Url; case "token": return Token; case "account": return Account;
                case "accessKey": return AccessKey; case "secretKey": return SecretKey; case "bucket": return Bucket; case "region": return Region;
                case "publicUrl": return PublicUrl; case "prefix": return Prefix; case "field": return Field; case "headers": return Headers;
                case "jsonPath": return JsonPath; case "hotkey": return Hotkey; case "allowHttp": return AllowHttp; case "title": return Title; case "description": return Description; case "color": return Color; case "footer": return Footer; case "footerIcon": return FooterIcon; case "method": return Method; case "urlParams": return UrlParams; case "bodyType": return BodyType; case "bodyArgs": return BodyArgs; case "bodyText": return BodyText; case "urlTemplate": return UrlTemplate; case "thumbUrl": return ThumbUrl; case "deleteUrl": return DeleteUrl; case "errorMsg": return ErrorMsg; case "zFormat": return ZFormat; case "zExtensionless": return ZExtensionless; case "zOriginalName": return ZOriginalName; case "zCompressType": return ZCompressType; case "zCompressQuality": return ZCompressQuality; case "zMaxViews": return ZMaxViews; case "zExpiry": return ZExpiry; case "zPassword": return ZPassword; case "zDomain": return ZDomain; case "zFolder": return ZFolder;
            }
            return "";
        }

        public void Set(string f, string v)
        {
            switch (f)
            {
                case "type": Type = v; break; case "name": Name = v; break; case "url": Url = v; break; case "token": Token = v; break; case "account": Account = v; break;
                case "accessKey": AccessKey = v; break; case "secretKey": SecretKey = v; break; case "bucket": Bucket = v; break; case "region": Region = v; break;
                case "publicUrl": PublicUrl = v; break; case "prefix": Prefix = v; break; case "field": Field = v; break; case "headers": Headers = v; break;
                case "jsonPath": JsonPath = v; break; case "hotkey": Hotkey = v; break; case "allowHttp": AllowHttp = v; break; case "title": Title = v; break; case "description": Description = v; break; case "color": Color = v; break; case "footer": Footer = v; break; case "footerIcon": FooterIcon = v; break; case "method": Method = v; break; case "urlParams": UrlParams = v; break; case "bodyType": BodyType = v; break; case "bodyArgs": BodyArgs = v; break; case "bodyText": BodyText = v; break; case "urlTemplate": UrlTemplate = v; break; case "thumbUrl": ThumbUrl = v; break; case "deleteUrl": DeleteUrl = v; break; case "errorMsg": ErrorMsg = v; break; case "zFormat": ZFormat = v; break; case "zExtensionless": ZExtensionless = v; break; case "zOriginalName": ZOriginalName = v; break; case "zCompressType": ZCompressType = v; break; case "zCompressQuality": ZCompressQuality = v; break; case "zMaxViews": ZMaxViews = v; break; case "zExpiry": ZExpiry = v; break; case "zPassword": ZPassword = v; break; case "zDomain": ZDomain = v; break; case "zFolder": ZFolder = v; break;
            }
        }

        public static readonly string[] Fields = { "type", "name", "url", "token", "account", "accessKey", "secretKey", "bucket", "region", "publicUrl", "prefix", "field", "headers", "jsonPath", "hotkey", "allowHttp", "title", "description", "color", "footer", "footerIcon", "method", "urlParams", "bodyType", "bodyArgs", "bodyText", "urlTemplate", "thumbUrl", "deleteUrl", "errorMsg", "zFormat", "zExtensionless", "zOriginalName", "zCompressType", "zCompressQuality", "zMaxViews", "zExpiry", "zPassword", "zDomain", "zFolder" };

        public Destination Clone() { return (Destination)MemberwiseClone(); }
    }
}
