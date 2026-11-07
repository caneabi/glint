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
    // Catches Print Screen before Windows hands it to the Snipping Tool.
    static class KeyHook
    {
        public static readonly Dictionary<Keys, Action> Bindings = new Dictionary<Keys, Action>();
        public static Action<Keys> Recorder;
        static IntPtr hook;
        static Native.HookProc proc;
        static Control invoker;
        static bool swallowing;
        static int swallowedKey;
        public static Func<Keys, Action> Intercept;

        // Printable without modifiers, so it could be typing.
        public static bool IsTextKey(Keys k)
        {
            if ((k & Keys.Modifiers & ~Keys.Shift) != 0) return false;
            var c = k & Keys.KeyCode;
            return (c >= Keys.A && c <= Keys.Z) || (c >= Keys.D0 && c <= Keys.D9) || (c >= Keys.NumPad0 && c <= Keys.Divide) || c == Keys.Space || (c >= Keys.OemSemicolon && c <= Keys.Oem102);
        }

        static IntPtr Other(int vk, IntPtr w)
        {
            int msg = w.ToInt32();
            if (msg == 0x101 || msg == 0x105)
            {
                if (swallowedKey != vk) return IntPtr.Zero;
                swallowedKey = 0;
                return new IntPtr(1);
            }
            if (swallowedKey == vk) return new IntPtr(1);
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x5B || vk == 0x5C) return IntPtr.Zero;
            var hook2 = Intercept;
            if (hook2 == null) return IntPtr.Zero;
            Keys combo = (Keys)vk;
            if (Down(0x11)) combo |= Keys.Control;
            if (Down(0x10)) combo |= Keys.Shift;
            if (Down(0x12)) combo |= Keys.Alt;
            var run = hook2(combo);
            if (run == null) return IntPtr.Zero;
            swallowedKey = vk;
            invoker.BeginInvoke(run);
            return new IntPtr(1);
        }

        public static void Install()
        {
            if (hook != IntPtr.Zero) return;
            invoker = new Control();
            var force = invoker.Handle;
            proc = Callback;
            hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0);
        }

        public static void Uninstall()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }

        static bool Down(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        static IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && Marshal.ReadInt32(l) != 0x2C)
            {
                var handled = Other(Marshal.ReadInt32(l), w);
                if (handled != IntPtr.Zero) return handled;
            }
            if (code >= 0 && Marshal.ReadInt32(l) == 0x2C)
            {
                int msg = w.ToInt32();
                if (msg == 0x101 || msg == 0x105)
                {
                    if (swallowing) { swallowing = false; return new IntPtr(1); }
                }
                else if (swallowing) return new IntPtr(1); // key repeat
                else
                {
                    Keys combo = Keys.PrintScreen;
                    if (Down(0x11)) combo |= Keys.Control;
                    if (Down(0x10)) combo |= Keys.Shift;
                    if (Down(0x12)) combo |= Keys.Alt;
                    Action action = null;
                    var rec = Recorder;
                    if (rec != null) action = () => rec(combo);
                    else Bindings.TryGetValue(combo, out action);
                    if (action != null)
                    {
                        swallowing = true;
                        invoker.BeginInvoke(action);
                        return new IntPtr(1);
                    }
                }
            }
            return Native.CallNextHookEx(hook, code, w, l);
        }
    }
}
