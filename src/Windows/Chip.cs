using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Glint
{
    static class Chip
    {
        public static void Draw(Graphics g, string text, Rectangle area, string key = null)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var f = new Font("Segoe UI", 9f))
            using (var kf = new Font("Segoe UI Semibold", 8f))
            {
                var ts = g.MeasureString(text, f);
                var ks = key == null ? SizeF.Empty : g.MeasureString(key, kf);
                float keyW = key == null ? 0 : ks.Width + 8;
                float w = ts.Width + 16 + (key == null ? 0 : keyW + 6);
                var r = new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - 28) / 2, w, 28);
                using (var path = Renderer.Round(r, 14)) using (var b = new SolidBrush(Color.FromArgb(232, 28, 28, 28))) g.FillPath(b, path);
                g.DrawString(text, f, Brushes.White, r.X + 8, r.Y + (28 - ts.Height) / 2);
                if (key != null)
                {
                    var kr = new RectangleF(r.Right - keyW - 7, r.Y + 5, keyW, 18);
                    using (var path = Renderer.Round(kr, 8)) using (var b = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillPath(b, path);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(key, kf, Brushes.White, kr, sf);
                }
            }
            g.SmoothingMode = old;
        }
    }
}
