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
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetDefaultDllDirectories(0x800); // LOAD_LIBRARY_SEARCH_SYSTEM32
            Native.SetProcessDpiAwarenessContext(new IntPtr(-3));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Toast.Error(L.T("Unerwarteter Fehler"), "Unbehandelte Ausnahme", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Schwerer Fehler", e.ExceptionObject as Exception);
            Settings.Load();
            L.Init(Settings.Current.Language);
#if SELFTEST
            if (args.Length == 2 && args[0] == "--selftest") { SelfTest.Run(args[1]); return; }
            if (args.Length == 3 && args[0] == "--shot") { SelfTest.Shot(args[1], args[2]); return; }
            if (args.Length == 2 && args[0] == "--rec-test") { SelfTest.RecTest(args[1]); return; }
            if (args.Length == 2 && args[0] == "--brand") { SelfTest.Brand(args[1]); return; }
            if (args.Length == 2 && args[0] == "--upload-test") { SelfTest.UploadTest(args[1]); return; }
            if (args.Length == 2 && args[0] == "--menu-shot") { SelfTest.MenuShot(args[1]); return; }
#endif
            if (args.Length == 1 && args[0] == "--uninstall") { Installer.Uninstall(); return; }
            if (Settings.Current.SetupDone && !Installer.RunningInstalled && Installer.CurrentIsNewer()) { Installer.ApplyUpdate(); return; }
            Imaging.CleanTemp(true);
            bool created, relaunch = false, afterSetup = false;
            using (new Mutex(true, Installer.MutexName, out created))
            {
                if (!created)
                {
                    // Tray icon may be hidden, so bring up settings instead.
                    System.Threading.EventWaitHandle other;
                    if (System.Threading.EventWaitHandle.TryOpenExisting(Tray.ShowEventName, out other)) using (other) other.Set();
                    return;
                }
                if (!Settings.Current.SetupDone)
                {
                    using (var w = new SetupWizard()) { w.ShowDialog(); relaunch = w.Relaunch; }
                    afterSetup = true;
                }
                bool open = afterSetup || Array.IndexOf(args, "--settings") >= 0;
                if (!relaunch) Application.Run(new Tray(Array.FindAll(args, a => !a.StartsWith("--")), open));
            }
            if (relaunch || Tray.RelaunchRequested) System.Diagnostics.Process.Start(Installer.InstalledExe, "--settings");
        }
    }
}
