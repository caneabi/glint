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
    class HotkeySink : NativeWindow
    {
        public Action<int> Pressed;
        public HotkeySink() { CreateHandle(new CreateParams()); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x312 && Pressed != null) Pressed(m.WParam.ToInt32());
            base.WndProc(ref m);
        }
    }
}
