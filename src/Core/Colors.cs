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
    static class Colors
    {
        static double Lin(int v) { double c = v / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static readonly string[] Formats = { "hex", "rgb", "hsl", "hsb", "oklch" };

        public static string Format(Color c, string f)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (f == "rgb") return "rgb(" + c.R + ", " + c.G + ", " + c.B + ")";
            if (f == "hsl")
                return string.Format(inv, "hsl({0:0}, {1:0}%, {2:0}%)", c.GetHue(), c.GetSaturation() * 100, c.GetBrightness() * 100);
            if (f == "hsb")
            {
                double max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0, min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255.0;
                return string.Format(inv, "hsb({0:0}, {1:0}%, {2:0}%)", c.GetHue(), max == 0 ? 0 : (max - min) / max * 100, max * 100);
            }
            if (f == "oklch")
            {
                double r = Lin(c.R), g = Lin(c.G), b = Lin(c.B);
                double l = Math.Pow(.4122214708 * r + .5363325363 * g + .0514459929 * b, 1 / 3.0);
                double m = Math.Pow(.2119034982 * r + .6806995451 * g + .1073969566 * b, 1 / 3.0);
                double s = Math.Pow(.0883024619 * r + .2817188376 * g + .6299787005 * b, 1 / 3.0);
                double L = .2104542553 * l + .793617785 * m - .0040720468 * s;
                double A = 1.9779984951 * l - 2.428592205 * m + .4505937099 * s;
                double B = .0259040371 * l + .7827717662 * m - .808675766 * s;
                double C = Math.Sqrt(A * A + B * B), H = Math.Atan2(B, A) * 180 / Math.PI;
                if (H < 0) H += 360;
                return string.Format(inv, "oklch({0:0.###} {1:0.###} {2:0.#})", L, C, C < .0005 ? 0 : H);
            }
            return Hex(c);
        }
        // Main colors of an image: coarse buckets by count, near duplicates merged.
        public static List<Color> Palette(Bitmap b, int max = 6)
        {
            var counts = new Dictionary<int, int>();
            var sums = new Dictionary<int, long[]>();
            int step = Math.Max(1, (int)Math.Sqrt((double)b.Width * b.Height / 12000));
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[b.Width];
                for (int y = 0; y < b.Height; y += step)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, b.Width);
                    for (int x = 0; x < b.Width; x += step)
                    {
                        int v = row[x], r = (v >> 16) & 255, g = (v >> 8) & 255, bl = v & 255;
                        int key = (r >> 4) << 8 | (g >> 4) << 4 | (bl >> 4);
                        int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
                        long[] s; if (!sums.TryGetValue(key, out s)) sums[key] = s = new long[3];
                        s[0] += r; s[1] += g; s[2] += bl;
                    }
                }
            }
            finally { b.UnlockBits(d); }
            var result = new List<Color>();
            foreach (var kv in counts.OrderByDescending(k => k.Value))
            {
                var s = sums[kv.Key];
                var c = Color.FromArgb((int)(s[0] / kv.Value), (int)(s[1] / kv.Value), (int)(s[2] / kv.Value));
                if (result.All(o => Imaging.ColorDiff(o, c) > 60)) result.Add(c);
                if (result.Count >= max) break;
            }
            return result;
        }

        public static double Wcag(Color a, Color b)
        {
            Func<Color, double> lum = c => .2126 * Lin(c.R) + .7152 * Lin(c.G) + .0722 * Lin(c.B);
            double x = lum(a), y = lum(b);
            return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
        }
        // APCA 0.1.9
        public static double Apca(Color text, Color bg)
        {
            Func<Color, double> y = c => .2126729 * Math.Pow(c.R / 255.0, 2.4) + .7151522 * Math.Pow(c.G / 255.0, 2.4) + .0721750 * Math.Pow(c.B / 255.0, 2.4);
            Func<double, double> clamp = v => v > .022 ? v : v + Math.Pow(.022 - v, 1.414);
            double t = clamp(y(text)), b = clamp(y(bg));
            if (Math.Abs(b - t) < .0005) return 0;
            double s;
            if (b > t) { s = (Math.Pow(b, .56) - Math.Pow(t, .57)) * 1.14; return s < .1 ? 0 : (s - .027) * 100; }
            s = (Math.Pow(b, .65) - Math.Pow(t, .62)) * 1.14;
            return s > -.1 ? 0 : (s + .027) * 100;
        }
    }
}
