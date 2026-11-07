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
    static class ScrollCapture
    {
        public static async Task<Bitmap> Run(Rectangle r, int maxHeight)
        {
            var home = Cursor.Position;
            Native.GetAsyncKeyState(0x1B); // forget older presses
            Native.SetCursorPos(r.X + r.Width / 2, r.Y + r.Height / 2);
            using (var st = new Stitcher())
            {
                try
                {
                    st.Add(Imaging.Grab(r));
                    int stale = 0;
                    while (st.Height < maxHeight)
                    {
                        Native.mouse_event(0x0800, 0, 0, -240, IntPtr.Zero);
                        if (await EscDuring(320)) break;
                        if (st.Add(Imaging.Grab(r))) stale = 0;
                        else if (++stale >= 2) break;
                    }
                }
                finally { Native.SetCursorPos(home.X, home.Y); }
                return st.Result();
            }
        }

        // Polls often, a quick tap would slip through otherwise.
        static async Task<bool> EscDuring(int ms)
        {
            for (int t = 0; t < ms; t += 20)
            {
                if (Native.GetAsyncKeyState(0x1B) != 0) return true;
                await Task.Delay(20);
            }
            return false;
        }
    }
}
