using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Glint
{
    static class Imaging
    {
        // Glass windows hide from capture so their blur doesn't see itself. Real screenshots bring them back.
        public static Bitmap Grab(Rectangle r, bool includeGlass = true)
        {
            var b = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var t = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb))
            {
                if (includeGlass) Glass.SetCapturable(true);
                using (var g = Graphics.FromImage(t))
                {
                    // CopyFromScreen rejects CAPTUREBLT.
                    IntPtr screen = Native.GetDC(IntPtr.Zero), dest = g.GetHdc();
                    bool ok = Native.BitBlt(dest, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020 | 0x40000000);
                    g.ReleaseHdc(dest);
                    Native.ReleaseDC(IntPtr.Zero, screen);
                    if (includeGlass) Glass.SetCapturable(false);
                    if (!ok) throw new InvalidOperationException("Bildschirmaufnahme fehlgeschlagen");
                }
                using (var g = Graphics.FromImage(b)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(t, 0, 0); }
            }
            return b;
        }

        static bool? win11;
        static bool Win11
        {
            get
            {
                if (!win11.HasValue)
                {
                    int build = 0;
                    var v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "0") as string;
                    int.TryParse(v, out build);
                    win11 = build >= 22000;
                }
                return win11.Value;
            }
        }

        static bool IsBlank(Bitmap b)
        {
            var first = b.GetPixel(b.Width / 2, b.Height / 2).ToArgb();
            if ((first & 0xFFFFFF) != 0) return false;
            for (int y = 0; y < b.Height; y += Math.Max(1, b.Height / 24))
                for (int x = 0; x < b.Width; x += Math.Max(1, b.Width / 24))
                    if (b.GetPixel(x, y).ToArgb() != first) return false;
            return true;
        }

        static string WallpaperPath()
        {
            string themed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (File.Exists(themed)) return themed;
            var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", "") as string;
            return !string.IsNullOrEmpty(v) && File.Exists(v) ? v : null;
        }

        public static Bitmap WallpaperThumb(int width)
        {
            try
            {
                var p = WallpaperPath();
                if (p == null) return null;
                using (var img = Image.FromFile(p))
                {
                    var b = new Bitmap(width, Math.Max(1, width * img.Height / img.Width));
                    using (var g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, 0, 0, b.Width, b.Height);
                    }
                    return b;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is OutOfMemoryException || ex is UnauthorizedAccessException) { Log.Error("Wallpaper", ex); return null; }
        }

        // Puts a captured window on a background, with a soft shadow.
        public static Bitmap Decorate(Bitmap win, Rectangle screenRect, int radius, string mode, string colorHex, float dpiScale)
        {
            int margin = (int)Math.Round(44 * dpiScale);
            var r = new Bitmap(win.Width + margin * 2, win.Height + margin * 2, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(r))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color color;
                if (!ColorPicker.TryParse(colorHex, out color)) color = Color.FromArgb(64, 68, 72);
                if (mode == "color") g.Clear(color);
                else if (mode == "wallpaper")
                {
                    g.Clear(color);
                    try
                    {
                        var p = WallpaperPath();
                        if (p != null)
                            using (var img = Image.FromFile(p))
                            {
                                var sc = Screen.FromRectangle(screenRect).Bounds;
                                float k = Math.Max((float)sc.Width / img.Width, (float)sc.Height / img.Height);
                                float ox = (sc.Width - img.Width * k) / 2 + sc.X, oy = (sc.Height - img.Height * k) / 2 + sc.Y;
                                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g.DrawImage(img, ox - screenRect.X + margin, oy - screenRect.Y + margin, img.Width * k, img.Height * k);
                            }
                    }
                    catch (Exception ex) when (ex is IOException || ex is OutOfMemoryException || ex is UnauthorizedAccessException) { Log.Error("Wallpaper", ex); }
                }
                var rect = new RectangleF(margin, margin, win.Width, win.Height);
                int blur = (int)Math.Round(20 * dpiScale);
                for (int i = blur; i > 0; i--)
                {
                    var sr = RectangleF.Inflate(rect, i, i);
                    sr.Offset(0, 4 * dpiScale);
                    using (var path = Renderer.Round(sr, Math.Max(2, radius * 2) + i * 2)) using (var b = new SolidBrush(Color.FromArgb(5, 0, 0, 0))) g.FillPath(b, path);
                }
                using (var tb = new TextureBrush(win, WrapMode.Clamp))
                {
                    tb.TranslateTransform(margin, margin);
                    if (radius > 0) using (var path = Renderer.Round(rect, radius * 2)) { g.CompositingMode = mode == "transparent" ? CompositingMode.SourceCopy : CompositingMode.SourceOver; g.FillPath(tb, path); g.CompositingMode = CompositingMode.SourceOver; }
                    else g.FillRectangle(tb, rect);
                }
            }
            return r;
        }

        // PrintWindow leaves nothing outside rounded corners, fill with the edge color.
        public static void FillCorners(Bitmap b, int r)
        {
            if (b.Width < r * 2 + 2 || b.Height < r * 2 + 2) return;
            foreach (bool right in new[] { false, true })
                foreach (bool bottom in new[] { false, true })
                    for (int dy = 0; dy < r; dy++)
                        for (int dx = 0; dx < r; dx++)
                        {
                            double d = Math.Sqrt((dx + .5) * (dx + .5) + (dy + .5) * (dy + .5));
                            if (d <= r - 1.5) continue;
                            double k = (r - 2.5) / d;
                            int sx = (int)((dx + .5) * k), sy = (int)((dy + .5) * k);
                            Func<int, int> mx = v => right ? b.Width - r + v : r - 1 - v;
                            Func<int, int> my = v => bottom ? b.Height - r + v : r - 1 - v;
                            var c = b.GetPixel(mx(sx), my(sy));
                            b.SetPixel(mx(dx), my(dy), Color.FromArgb(255, c));
                        }
        }

        public static Bitmap GrabWindow(IntPtr h)
        {
            var bounds = Native.WindowBounds(h);
            Native.RECT wr;
            if (bounds.Width <= 0 || !Native.GetWindowRect(h, out wr)) return Grab(bounds);
            var full = Rectangle.FromLTRB(wr.L, wr.T, wr.R, wr.B);
            bool ok;
            using (var raw = new Bitmap(full.Width, full.Height, PixelFormat.Format32bppRgb))
            {
                using (var g = Graphics.FromImage(raw))
                {
                    var dc = g.GetHdc();
                    ok = Native.PrintWindow(h, dc, 2); // PW_RENDERFULLCONTENT
                    g.ReleaseHdc(dc);
                }
                if (ok)
                {
                    var crop = new Rectangle(bounds.X - full.X, bounds.Y - full.Y, bounds.Width, bounds.Height);
                    var result = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(result))
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.DrawImage(raw, new Rectangle(0, 0, bounds.Width, bounds.Height), crop, GraphicsUnit.Pixel);
                    }
                    bool rounded = Win11 && !Native.IsZoomed(h);
                    int radius = rounded ? (int)Math.Ceiling(8 * Native.GetDpiForWindow(h) / 96.0) + 1 : 0;
                    if (!IsBlank(result) && Settings.Current.WindowBg != "trim")
                    {
                        var framed = Decorate(result, bounds, radius, Settings.Current.WindowBg, Settings.Current.WindowBgColor, Native.GetDpiForWindow(h) / 96f);
                        result.Dispose();
                        return framed;
                    }
                    if (rounded) FillCorners(result, radius);
                    if (!IsBlank(result)) return result;
                    result.Dispose();
                }
            }
            return Grab(bounds); // e.g. fullscreen games
        }

        public static Bitmap Crop(Bitmap src, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            var b = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), r, GraphicsUnit.Pixel);
            }
            return b;
        }

        public static Bitmap Normalize(Image src)
        {
            var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            return b;
        }

        public static Bitmap Scaled(Bitmap b)
        {
            int pct = Settings.Current.OutputScale;
            if (pct <= 0 || pct >= 100) return b;
            var r = new Bitmap(Math.Max(1, b.Width * pct / 100), Math.Max(1, b.Height * pct / 100), PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(r))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(b, 0, 0, r.Width, r.Height);
            }
            return r;
        }

        // Bigger images are encoded off the UI thread.
        const long LargeImage = 8000000;
        static bool IsLarge(Bitmap b) { return (long)b.Width * b.Height > LargeImage; }
        static Bitmap Clone(Bitmap b) { return Crop(b, new Rectangle(0, 0, b.Width, b.Height)); }

        static async void CopyLarge(Bitmap own)
        {
            Bitmap s = null;
            try
            {
                MemoryStream ms = null;
                await Task.Run(() => { s = Scaled(own); ms = new MemoryStream(); s.Save(ms, ImageFormat.Png); });
                CopyEncoded(s, ms);
                Sounds.Play(Sounds.Kind.Copy);
            }
            catch (Exception ex) { Toast.Error(L.T("Kopieren fehlgeschlagen"), "Kopieren", ex); }
            finally { if (s != null && s != own) s.Dispose(); own.Dispose(); }
        }

        static async void SaveLarge(Bitmap own, string path)
        {
            try { await Task.Run(() => SaveFile(own, path)); }
            catch (Exception ex)
            {
                try { File.Delete(path); } catch (IOException) { }
                Toast.Error(L.T("Speichern fehlgeschlagen"), "Speichern", ex);
            }
            finally { own.Dispose(); }
        }

        public static void Copy(Bitmap b)
        {
            if (IsLarge(b)) { CopyLarge(Clone(b)); return; }
            var s = Scaled(b);
            try { CopyRaw(s); } finally { if (s != b) s.Dispose(); }
            Sounds.Play(Sounds.Kind.Copy);
        }

        public static string SaveFile(Bitmap b, string path)
        {
            var s = Scaled(b);
            try { return WriteFile(s, path); } finally { if (s != b) s.Dispose(); }
        }

        static void CopyRaw(Bitmap b)
        {
            var ms = new MemoryStream();
            b.Save(ms, ImageFormat.Png);
            CopyEncoded(b, ms);
        }

        static void CopyEncoded(Bitmap b, MemoryStream ms)
        {
            var data = new DataObject();
            data.SetData("PNG", false, ms);
            using (var flat = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImageUnscaled(b, 0, 0); }
                data.SetImage(flat);
                Clipboard.SetDataObject(data, true);
            }
        }

        public static bool LooksPhotographic(Bitmap b)
        {
            var seen = new HashSet<int>();
            int stepX = Math.Max(1, b.Width / 200), stepY = Math.Max(1, b.Height / 200), total = 0;
            for (int y = 0; y < b.Height; y += stepY)
                for (int x = 0; x < b.Width; x += stepX) { seen.Add(b.GetPixel(x, y).ToArgb()); total++; }
            return total > 0 && seen.Count > total * 0.45;
        }

        static string WriteFile(Bitmap b, string path)
        {
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                using (var flat = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImageUnscaled(b, 0, 0); }
                    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    var ps = new EncoderParameters(1);
                    ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(1, Math.Min(100, Settings.Current.JpegQuality)));
                    flat.Save(path, codec, ps);
                }
            }
            else if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) File.WriteAllBytes(path, WebP.Encode(b));
            else b.Save(path, ImageFormat.Png);
            return path;
        }

        const long MaxOpenBytes = 512L * 1024 * 1024;

        static string tempDir;
        public static string TempFile(string ext)
        {
            if (tempDir == null)
            {
                tempDir = Path.Combine(Path.GetTempPath(), "Glint-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
            }
            return Path.Combine(tempDir, "Glint-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);
        }

        public static void CleanTemp(bool staleOnly)
        {
            foreach (var d in Directory.GetDirectories(Path.GetTempPath(), "Glint-*"))
            {
                try
                {
                    if (staleOnly ? Directory.GetLastWriteTime(d) < DateTime.Now.AddDays(-1) : d == tempDir) Directory.Delete(d, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public static Bitmap Load(string path)
        {
            if (new FileInfo(path).Length > MaxOpenBytes) throw new InvalidOperationException("Datei ist zu groß");
            if (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
            {
                try { return WebP.Decode(path); }
                catch (Exception ex)
                {
                    if (ex is NotSupportedException || ex is System.IO.FileFormatException) throw new InvalidOperationException("WebP-Bilderweiterungen von Microsoft fehlen", ex);
                    throw;
                }
            }
            using (var img = Image.FromFile(path)) return Normalize(img);
        }

        public static string QuickSave(Bitmap b)
        {
            var s = Settings.Current;
            Directory.CreateDirectory(s.SaveDir);
            bool jpg = s.Format == "jpg" || (s.Format == "auto" && b.Width * b.Height > 0 && LooksPhotographic(b));
            string name = FileNames.ForSave(b.Width, b.Height) + (s.Format == "webp" ? ".webp" : jpg ? ".jpg" : ".png");
            string path = FileNames.Unique(Path.Combine(s.SaveDir, name));
            if (!IsLarge(b)) return SaveFile(b, path);
            File.Create(path).Dispose(); // reserves the name while it's written
            SaveLarge(Clone(b), path);
            return path;
        }

        public static Rectangle Trim(Bitmap b, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, b.Width, b.Height));
            if (r.Width < 3 || r.Height < 3) return r;
            Color bg = b.GetPixel(r.Left, r.Top);
            Func<int, bool> rowBlank = y => { for (int x = r.Left; x < r.Right; x++) if (ColorDiff(b.GetPixel(x, y), bg) > 24) return false; return true; };
            Func<int, bool> colBlank = x => { for (int y = r.Top; y < r.Bottom; y++) if (ColorDiff(b.GetPixel(x, y), bg) > 24) return false; return true; };
            int t = r.Top, bo = r.Bottom - 1, l = r.Left, ri = r.Right - 1;
            while (t < bo && rowBlank(t)) t++;
            while (bo > t && rowBlank(bo)) bo--;
            while (l < ri && colBlank(l)) l++;
            while (ri > l && colBlank(ri)) ri--;
            return Rectangle.FromLTRB(l, t, ri + 1, bo + 1);
        }

        public static int ColorDiff(Color a, Color c)
        {
            return Math.Abs(a.R - c.R) + Math.Abs(a.G - c.G) + Math.Abs(a.B - c.B);
        }

        public static Color Average(Bitmap b, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, b.Width, b.Height));
            long R = 0, G = 0, B = 0, n = 0;
            int sx = Math.Max(1, r.Width / 200), sy = Math.Max(1, r.Height / 200);
            for (int y = r.Top; y < r.Bottom; y += sy)
                for (int x = r.Left; x < r.Right; x += sx) { var c = b.GetPixel(x, y); R += c.R; G += c.G; B += c.B; n++; }
            if (n == 0) return Color.Black;
            return Color.FromArgb((int)(R / n), (int)(G / n), (int)(B / n));
        }

        public static Color Darkest(Bitmap b, Point p)
        {
            Color best = Color.White; double lum = 1e9;
            for (int y = Math.Max(0, p.Y - 10); y < Math.Min(b.Height, p.Y + 10); y++)
                for (int x = Math.Max(0, p.X - 10); x < Math.Min(b.Width, p.X + 10); x++)
                {
                    var c = b.GetPixel(x, y);
                    double l = c.R * .299 + c.G * .587 + c.B * .114;
                    if (l < lum) { lum = l; best = c; }
                }
            return best;
        }
    }
}
