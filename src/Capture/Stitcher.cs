using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinOcr = Windows.Media.Ocr;
using WinImg = Windows.Graphics.Imaging;

namespace Glint
{
    // Owns every frame passed to Add.
    class Stitcher : IDisposable
    {
        readonly List<Bitmap> strips = new List<Bitmap>();
        Bitmap first, last;
        int[] prev;
        int top = -1, bot = -1, height;
        public int Height { get { return height; } }

        static int[] Hashes(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var res = new int[b.Height];
                int from = b.Width / 10, to = b.Width - b.Width / 10; // skip scrollbars
                var row = new int[to - from];
                for (int y = 0; y < b.Height; y++)
                {
                    Marshal.Copy(d.Scan0 + y * d.Stride + from * 4, row, 0, row.Length);
                    int h = 17;
                    foreach (var v in row) h = unchecked(h * 31 + v);
                    res[y] = h;
                }
                return res;
            }
            finally { b.UnlockBits(d); }
        }

        // false when the frame is unchanged or has no overlap.
        public bool Add(Bitmap b)
        {
            var cur = Hashes(b);
            if (first == null) { first = last = b; prev = cur; height = b.Height; return true; }
            int h = cur.Length, t = 0, u = 0;
            while (t < h && cur[t] == prev[t]) t++;
            if (t == h) { b.Dispose(); return false; }
            while (u < h - t && cur[h - 1 - u] == prev[h - 1 - u]) u++;
            if (top < 0)
            {
                top = t; bot = u;
                height = h - bot;
            }
            int a = Math.Min(top, t), z = Math.Min(bot, u), m = h - a - z;
            int bestDy = -1, bestScore = 0;
            for (int dy = 1; dy <= m - 8; dy++)
            {
                int overlap = m - dy, score = 0;
                for (int i = 0; i < overlap; i++) if (prev[a + dy + i] == cur[a + i]) score++;
                if (score >= overlap * .97 && score > bestScore) { bestScore = score; bestDy = dy; }
            }
            if (bestDy < 0) { b.Dispose(); return false; }
            strips.Add(Imaging.Crop(b, new Rectangle(0, h - z - bestDy, b.Width, bestDy)));
            height += bestDy;
            prev = cur;
            if (last != first) last.Dispose();
            last = b;
            return true;
        }

        public void Dispose()
        {
            foreach (var s in strips) s.Dispose();
            strips.Clear();
            if (last != null && last != first) last.Dispose();
            if (first != null) first.Dispose();
            first = last = null;
        }

        public Bitmap Result()
        {
            if (first == null) return null;
            if (strips.Count == 0) return Imaging.Crop(first, new Rectangle(0, 0, first.Width, first.Height));
            int footer = first.Height - (first.Height - Math.Max(bot, 0));
            var r = new Bitmap(first.Width, height + footer, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(r))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                int y = first.Height - footer;
                g.DrawImage(first, new Rectangle(0, 0, first.Width, y), new Rectangle(0, 0, first.Width, y), GraphicsUnit.Pixel);
                foreach (var s in strips) { g.DrawImageUnscaled(s, 0, y); y += s.Height; }
                if (footer > 0) g.DrawImage(last, new Rectangle(0, y, first.Width, footer), new Rectangle(0, last.Height - footer, first.Width, footer), GraphicsUnit.Pixel);
            }
            return r;
        }
    }
}
