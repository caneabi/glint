using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Glint
{
    // Lossless VP8L encoder.
    static class WebP
    {
        class BitWriter
        {
            public readonly MemoryStream S = new MemoryStream();
            ulong acc; int n;
            public void Put(int value, int bits)
            {
                acc |= (ulong)(uint)value << n;
                n += bits;
                while (n >= 8) { S.WriteByte((byte)acc); acc >>= 8; n -= 8; }
            }
            public void Flush() { if (n > 0) { S.WriteByte((byte)acc); acc = 0; n = 0; } }
        }

        class Code
        {
            public int[] Len, Bits;
            public void Emit(BitWriter w, int sym) { if (Len[sym] > 0) w.Put(Bits[sym], Len[sym]); }
        }

        public static byte[] Encode(Bitmap b)
        {
            int w = b.Width, h = b.Height;
            if (w > 16384 || h > 16384) throw new InvalidOperationException("WebP erlaubt höchstens 16384 px pro Seite");
            var px = new int[w * h];
            var d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w, w);
            b.UnlockBits(d);
            bool alpha = px.Any(v => ((uint)v >> 24) != 255);

            var bw = new BitWriter();
            bw.Put(0x2F, 8);
            bw.Put(w - 1, 14); bw.Put(h - 1, 14); bw.Put(alpha ? 1 : 0, 1); bw.Put(0, 3);

            // predictor transform, 16x16 tiles
            const int TileBits = 4;
            int tw = (w + (1 << TileBits) - 1) >> TileBits, th = (h + (1 << TileBits) - 1) >> TileBits;
            var modes = new int[tw * th];
            var res = new int[px.Length];
            for (int ty = 0; ty < th; ty++)
                for (int tx = 0; tx < tw; tx++)
                {
                    int best = 1; long bestCost = long.MaxValue;
                    foreach (int mode in new[] { 1, 2, 0 })
                    {
                        long cost = 0;
                        for (int y = ty << TileBits; y < Math.Min(h, (ty + 1) << TileBits); y++)
                            for (int x = tx << TileBits; x < Math.Min(w, (tx + 1) << TileBits); x++)
                                cost += Cost(Sub(px[y * w + x], Predict(px, w, x, y, mode)));
                        if (cost < bestCost) { bestCost = cost; best = mode; }
                    }
                    modes[ty * tw + tx] = unchecked((int)0xFF000000u) | (best << 8);
                    for (int y = ty << TileBits; y < Math.Min(h, (ty + 1) << TileBits); y++)
                        for (int x = tx << TileBits; x < Math.Min(w, (tx + 1) << TileBits); x++)
                            res[y * w + x] = Sub(px[y * w + x], Predict(px, w, x, y, best));
                }
            bw.Put(1, 1); bw.Put(0, 2); bw.Put(TileBits - 2, 3);
            WriteImage(bw, modes, tw, 0, false);

            // subtract green
            bw.Put(1, 1); bw.Put(2, 2);
            for (int i = 0; i < res.Length; i++)
            {
                uint v = (uint)res[i];
                uint g = (v >> 8) & 255, r = ((v >> 16) - g) & 255, bl = (v - g) & 255;
                res[i] = (int)((v & 0xFF00FF00u) | (r << 16) | bl);
            }
            bw.Put(0, 1); // no more transforms
            WriteImage(bw, res, w, 10, true);
            bw.Flush();

            var payload = bw.S.ToArray();
            int pad = payload.Length & 1;
            using (var ms = new MemoryStream())
            using (var o = new BinaryWriter(ms))
            {
                o.Write(new[] { 'R', 'I', 'F', 'F' });
                o.Write(4 + 8 + payload.Length + pad);
                o.Write(new[] { 'W', 'E', 'B', 'P', 'V', 'P', '8', 'L' });
                o.Write(payload.Length);
                o.Write(payload);
                if (pad == 1) o.Write((byte)0);
                o.Flush();
                return ms.ToArray();
            }
        }

        static int Predict(int[] p, int w, int x, int y, int mode)
        {
            if (x == 0 && y == 0) return unchecked((int)0xFF000000u);
            if (y == 0) return p[x - 1];
            if (x == 0) return p[(y - 1) * w];
            if (mode == 1) return p[y * w + x - 1];
            if (mode == 2) return p[(y - 1) * w + x];
            return unchecked((int)0xFF000000u);
        }

        static int Sub(int a, int c)
        {
            uint x = (uint)a, y = (uint)c;
            uint aa = ((x >> 24) - (y >> 24)) & 255, rr = ((x >> 16) - (y >> 16)) & 255, gg = ((x >> 8) - (y >> 8)) & 255, bb = (x - y) & 255;
            return (int)((aa << 24) | (rr << 16) | (gg << 8) | bb);
        }

        static int Cost(int v)
        {
            Func<uint, int> one = c => c > 127 ? (int)(256 - c) : (int)c;
            uint u = (uint)v;
            return one((u >> 24) & 255) + one((u >> 16) & 255) + one((u >> 8) & 255) + one(u & 255);
        }

        static void Prefix(int value, out int prefix, out int extraBits, out int extra)
        {
            if (value <= 4) { prefix = value - 1; extraBits = 0; extra = 0; return; }
            int v = value - 1, high = 31;
            while (((v >> high) & 1) == 0) high--;
            int second = (v >> (high - 1)) & 1;
            extraBits = high - 1;
            extra = v & ((1 << extraBits) - 1);
            prefix = 2 * high + second;
        }

        // Tokens: A=0 literal (B=ARGB), A=1 cache (B=key), A<0 copy (len=-A, B=dist)
        static void WriteImage(BitWriter bw, int[] p, int width, int cacheBits, bool isMain)
        {
            int n = p.Length;
            var A = new List<int>(n / 4 + 16); var B = new List<int>(n / 4 + 16);
            int[] cache = cacheBits > 0 ? new int[1 << cacheBits] : null;
            bool[] used = cacheBits > 0 ? new bool[1 << cacheBits] : null;
            const int HashBits = 18, Chain = 24, MaxLen = 4096, MinLen = 3, MaxDist = (1 << 20) - 200;
            var head = new int[1 << HashBits];
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            var prev = new int[n];
            Func<int, int> hash = i => (int)(((uint)p[i] * 0x1E35A7BDu ^ (uint)p[i + 1] * 0x9E3779B1u) >> (32 - HashBits));
            Action<int> insert = i => { if (i + 1 < n) { int k = hash(i); prev[i] = head[k]; head[k] = i; } };
            Action<int> cachePut = i =>
            {
                if (cache == null) return;
                int k = (int)(((uint)p[i] * 0x1E35A7BDu) >> (32 - cacheBits));
                cache[k] = p[i]; used[k] = true;
            };
            Func<int, int, int> matchLen = (i, c) =>
            {
                int m = Math.Min(MaxLen, n - i), l = 0;
                while (l < m && p[i + l] == p[c + l]) l++;
                return l;
            };
            for (int i = 0; i < n; )
            {
                int bestLen = 0, bestDist = 0;
                foreach (int dist in new[] { 1, width })
                    if (dist > 0 && i - dist >= 0)
                    {
                        int l = matchLen(i, i - dist);
                        if (l > bestLen) { bestLen = l; bestDist = dist; }
                    }
                if (bestLen < 64 && i + 1 < n)
                {
                    int c = head[hash(i)], tries = Chain;
                    while (c >= 0 && tries-- > 0 && i - c <= MaxDist)
                    {
                        int l = matchLen(i, c);
                        if (l > bestLen) { bestLen = l; bestDist = i - c; }
                        c = prev[c];
                    }
                }
                if (bestLen >= MinLen)
                {
                    A.Add(-bestLen); B.Add(bestDist);
                    for (int k = 0; k < bestLen; k++) { insert(i + k); cachePut(i + k); }
                    i += bestLen;
                    continue;
                }
                bool hit = false;
                if (cache != null)
                {
                    int key = (int)(((uint)p[i] * 0x1E35A7BDu) >> (32 - cacheBits));
                    if (used[key] && cache[key] == p[i]) { A.Add(1); B.Add(key); hit = true; }
                }
                if (!hit) { A.Add(0); B.Add(p[i]); }
                insert(i); cachePut(i);
                i++;
            }

            int greenSize = 256 + 24 + (cache != null ? 1 << cacheBits : 0);
            var hg = new long[greenSize]; var hr = new long[256]; var hb = new long[256]; var ha = new long[256]; var hd = new long[40];
            int pre, eb, ex;
            for (int t = 0; t < A.Count; t++)
            {
                if (A[t] == 0)
                {
                    uint v = (uint)B[t];
                    hg[(v >> 8) & 255]++; hr[(v >> 16) & 255]++; hb[v & 255]++; ha[v >> 24]++;
                }
                else if (A[t] == 1) hg[256 + 24 + B[t]]++;
                else
                {
                    Prefix(-A[t], out pre, out eb, out ex); hg[256 + pre]++;
                    Prefix(B[t] + 120, out pre, out eb, out ex); hd[pre]++;
                }
            }

            if (cache != null) { bw.Put(1, 1); bw.Put(cacheBits, 4); } else bw.Put(0, 1);
            if (isMain) bw.Put(0, 1); // no meta prefix image
            var cg = WriteCode(bw, hg); var cr = WriteCode(bw, hr); var cb = WriteCode(bw, hb); var ca = WriteCode(bw, ha); var cd = WriteCode(bw, hd);

            for (int t = 0; t < A.Count; t++)
            {
                if (A[t] == 0)
                {
                    uint v = (uint)B[t];
                    cg.Emit(bw, (int)((v >> 8) & 255)); cr.Emit(bw, (int)((v >> 16) & 255)); cb.Emit(bw, (int)(v & 255)); ca.Emit(bw, (int)(v >> 24));
                }
                else if (A[t] == 1) cg.Emit(bw, 256 + 24 + B[t]);
                else
                {
                    Prefix(-A[t], out pre, out eb, out ex); cg.Emit(bw, 256 + pre); if (eb > 0) bw.Put(ex, eb);
                    Prefix(B[t] + 120, out pre, out eb, out ex); cd.Emit(bw, pre); if (eb > 0) bw.Put(ex, eb);
                }
            }
        }

        static int[] Lengths(long[] counts, int limit)
        {
            var len = new int[counts.Length];
            var syms = Enumerable.Range(0, counts.Length).Where(i => counts[i] > 0).ToList();
            if (syms.Count == 0) return len;
            if (syms.Count == 1) { len[syms[0]] = 1; return len; }
            var c = (long[])counts.Clone();
            while (true)
            {
                var leaves = syms.OrderBy(s => c[s]).ToList();
                int total = leaves.Count * 2 - 1;
                var weight = new long[total]; var parent = new int[total];
                for (int i = 0; i < leaves.Count; i++) weight[i] = c[leaves[i]];
                int li = 0, ni = leaves.Count, nn = leaves.Count;
                Func<int> take = () =>
                {
                    if (li < leaves.Count && (ni >= nn || weight[li] <= weight[ni])) return li++;
                    return ni++;
                };
                while (nn < total)
                {
                    int a = take(), b = take();
                    weight[nn] = weight[a] + weight[b];
                    parent[a] = nn; parent[b] = nn;
                    nn++;
                }
                int depthMax = 0;
                var depth = new int[total];
                for (int i = total - 2; i >= 0; i--) { depth[i] = depth[parent[i]] + 1; if (depth[i] > depthMax) depthMax = depth[i]; }
                if (depthMax <= limit)
                {
                    for (int i = 0; i < leaves.Count; i++) len[leaves[i]] = depth[i];
                    return len;
                }
                foreach (int s in syms) c[s] = Math.Max(1, c[s] / 2);
            }
        }

        static Code Canonical(int[] len)
        {
            var count = new int[17]; foreach (int l in len) count[l]++;
            count[0] = 0;
            var next = new int[17]; int code = 0;
            for (int b = 1; b <= 16; b++) { code = (code + count[b - 1]) << 1; next[b] = code; }
            var bits = new int[len.Length];
            for (int s = 0; s < len.Length; s++)
                if (len[s] > 0)
                {
                    int v = next[len[s]]++, r = 0;
                    for (int i = 0; i < len[s]; i++) r |= ((v >> i) & 1) << (len[s] - 1 - i);
                    bits[s] = r;
                }
            return new Code { Len = len, Bits = bits };
        }

        static readonly int[] ClOrder = { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        static Code WriteCode(BitWriter w, long[] hist)
        {
            var syms = Enumerable.Range(0, hist.Length).Where(i => hist[i] > 0).ToList();
            // simple code: up to two symbols below 256
            if (syms.Count <= 2 && syms.All(s => s < 256))
            {
                w.Put(1, 1);
                w.Put(Math.Max(0, syms.Count - 1), 1);
                w.Put(1, 1);
                w.Put(syms.Count > 0 ? syms[0] : 0, 8);
                var len2 = new int[hist.Length];
                if (syms.Count == 2) { w.Put(syms[1], 8); len2[syms[0]] = 1; len2[syms[1]] = 1; }
                return Canonical(len2); // a single symbol takes no bits
            }
            var len = Lengths(hist, 15);
            w.Put(0, 1);
            var tok = new List<int>(); var tex = new List<int>();
            for (int i = 0; i < len.Length; )
            {
                if (len[i] != 0) { tok.Add(len[i]); tex.Add(0); i++; continue; }
                int run = 0;
                while (i + run < len.Length && len[i + run] == 0 && run < 138) run++;
                if (run >= 11) { tok.Add(18); tex.Add(run - 11); }
                else if (run >= 3) { tok.Add(17); tex.Add(run - 3); }
                else { for (int k = 0; k < run; k++) { tok.Add(0); tex.Add(0); } }
                i += run;
            }
            var clHist = new long[19]; foreach (int t in tok) clHist[t]++;
            var clLen = Lengths(clHist, 7);
            var cl = Canonical(clLen);
            bool clSingle = clHist.Count(v => v > 0) == 1;
            int num = 19; while (num > 4 && clLen[ClOrder[num - 1]] == 0) num--;
            w.Put(num - 4, 4);
            for (int i = 0; i < num; i++) w.Put(clLen[ClOrder[i]], 3);
            w.Put(0, 1); // max_symbol = alphabet size
            for (int i = 0; i < tok.Count; i++)
            {
                if (!clSingle) cl.Emit(w, tok[i]);
                if (tok[i] == 17) w.Put(tex[i], 3);
                else if (tok[i] == 18) w.Put(tex[i], 7);
            }
            var code = Canonical(len);
            if (syms.Count == 1) { code.Len = new int[len.Length]; } // a single symbol takes no bits
            return code;
        }

        // Uses the codec installed in Windows.
        public static Bitmap Decode(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(dec.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var r = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                var d = r.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                conv.CopyPixels(System.Windows.Int32Rect.Empty, d.Scan0, d.Stride * h, d.Stride);
                r.UnlockBits(d);
                return r;
            }
        }
    }
}
