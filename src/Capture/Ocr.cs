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
    static class Ocr
    {
        public static async Task<string> Recognize(Bitmap source)
        {
            var engine = WinOcr.OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
            {
                var any = WinOcr.OcrEngine.AvailableRecognizerLanguages.FirstOrDefault();
                if (any != null) engine = WinOcr.OcrEngine.TryCreateFromLanguage(any);
            }
            if (engine == null) throw new InvalidOperationException("Kein OCR-Sprachpaket installiert (Windows-Einstellungen > Zeit und Sprache > Sprache: Texterkennung hinzufügen)");
            // Small crops read much better when upscaled.
            int f = Math.Min(source.Width, source.Height) < 300 ? 3 : 1;
            float maxDim = WinOcr.OcrEngine.MaxImageDimension;
            float fit = Math.Min(1f, maxDim / (Math.Max(source.Width, source.Height) * f));
            using (var scaled = new Bitmap(Math.Max(1, (int)(source.Width * f * fit)), Math.Max(1, (int)(source.Height * f * fit)), PixelFormat.Format24bppRgb))
            using (var ms = new MemoryStream())
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, scaled.Width, scaled.Height);
                }
                scaled.Save(ms, ImageFormat.Png);
                var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var writer = new Windows.Storage.Streams.DataWriter(ras);
                writer.WriteBytes(ms.ToArray());
                await Wait(writer.StoreAsync());
                writer.DetachStream();
                ras.Seek(0);
                var dec = await Wait(WinImg.BitmapDecoder.CreateAsync(ras));
                var soft = await Wait(dec.GetSoftwareBitmapAsync());
                var res = await Wait(engine.RecognizeAsync(soft));
                return string.Join(Environment.NewLine, res.Lines.Select(l => l.Text));
            }
        }

        static async Task<T> Wait<T>(Windows.Foundation.IAsyncOperation<T> op)
        {
            while (op.Status == Windows.Foundation.AsyncStatus.Started) await Task.Delay(5).ConfigureAwait(false);
            if (op.Status != Windows.Foundation.AsyncStatus.Completed) throw new InvalidOperationException("OCR-Fehler: " + op.ErrorCode);
            return op.GetResults();
        }
    }
}
