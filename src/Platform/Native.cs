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
    static class Native
    {
        [DllImport("user32")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32")] public static extern void mouse_event(uint f, int x, int y, int d, IntPtr e);
        [DllImport("user32")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int cy, uint f);
        [DllImport("user32")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("kernel32")] public static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("user32")] static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
        [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int Size; public uint Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public RECT CaretRect; }
        public static bool CaretActive()
        {
            var i = new GUITHREADINFO { Size = Marshal.SizeOf(typeof(GUITHREADINFO)) };
            return GetGUIThreadInfo(0, ref i) && i.Caret != IntPtr.Zero;
        }
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr h, int i);
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32")] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint thread);
        [DllImport("user32")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("user32")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
        [DllImport("kernel32")] public static extern IntPtr GetModuleHandle(string n);
        [DllImport("user32")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);
        [DllImport("user32")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref Point pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref BLENDFUNCTION b, int flags);
        [DllImport("user32")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
        [DllImport("gdi32")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO i, uint u, out IntPtr bits, IntPtr sec, uint off);
        [DllImport("dwmapi")] public static extern int DwmFlush();
        [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        [DllImport("dwmapi")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [StructLayout(LayoutKind.Sequential)] public struct MARGINS { public int L, R, T, B; }
        [StructLayout(LayoutKind.Sequential)] public struct TRACKMOUSEEVENT { public int Size; public uint Flags; public IntPtr Hwnd; public uint Hover; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFO { public int Size, Width, Height; public short Planes, BitCount; public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant; public int Colors; }
        public static void Rounded(IntPtr h) { int v = 2; DwmSetWindowAttribute(h, 33, ref v, 4); }
        [DllImport("user32")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32")] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
        [DllImport("dwmapi")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out int r, int s);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

        public static Rectangle WindowBounds(IntPtr h)
        {
            RECT r;
            if (DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))) != 0) return Rectangle.Empty;
            return Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        }

        public struct WinInfo { public IntPtr H; public Rectangle R; public bool Desktop; }

        // Top-level windows, topmost first.
        public static List<WinInfo> VisibleWindows()
        {
            var list = new List<WinInfo>();
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                int cloaked;
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                if ((GetWindowLong(h, -20) & 0x20) != 0) return true; // WS_EX_TRANSPARENT, overlays from other tools
                var b = WindowBounds(h);
                if (b.Width <= 20 || b.Height <= 20) return true;
                var cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                string c = cls.ToString();
                list.Add(new WinInfo { H = h, R = b, Desktop = c == "Progman" || c == "WorkerW" });
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }
}
