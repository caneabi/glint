using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Glint
{
    static class Installer
    {
        public static string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Glint");
        public static string StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        public static string DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        public static string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Glint";
        const string RunValue = "Glint";
        public const string MutexName = "Local\\Glint.SingleInstance.v1";

        // True when this exe is a newer build than the installed one.
        public static bool CurrentIsNewer()
        {
            if (!IsInstalled) return false;
            string[] parts = (FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion ?? "").Split('+');
            Version installed;
            if (!Version.TryParse(parts[0], out installed)) return false;
            var current = Updater.Current;
            var a = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            var b = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
            if (a != b) return a > b;
            string commit = Updater.CurrentCommit;
            return commit != "" && (parts.Length < 2 || !parts[1].StartsWith(commit, StringComparison.OrdinalIgnoreCase));
        }

        // Asks a running Glint to quit and waits until it's gone.
        static void StopRunning()
        {
            System.Threading.EventWaitHandle quit;
            if (System.Threading.EventWaitHandle.TryOpenExisting(Tray.QuitEventName, out quit)) using (quit) quit.Set();
            using (var m = new System.Threading.Mutex(false, MutexName))
            {
                try { if (m.WaitOne(6000)) m.ReleaseMutex(); }
                catch (System.Threading.AbandonedMutexException) { m.ReleaseMutex(); }
            }
        }

        public static void ApplyUpdate()
        {
            StopRunning();
            for (int i = 0; ; i++)
            {
                try { File.Copy(CurrentExe, InstalledExe, true); break; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (i < 5) { System.Threading.Thread.Sleep(300); continue; }
                    Log.Error("Update", ex);
                    MessageBox.Show(L.T("Glint läuft noch. Beende es und starte das Update erneut."), "Glint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            Log.Info("Aktualisiert auf " + Updater.Current);
            Process.Start(InstalledExe, "--settings");
        }

        public static string CurrentExe { get { return Assembly.GetEntryAssembly().Location; } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "Glint.exe"); } }
        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }
        public static bool RunningInstalled { get { return string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); } }

        public static bool Autostart
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunValue) != null; }
        }

        public static void SetAutostart(bool on, string exe = null)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, "\"" + (exe ?? (IsInstalled ? InstalledExe : CurrentExe)) + "\"");
                else k.DeleteValue(RunValue, false);
            }
        }

        static void Shortcut(string lnk, string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object s = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            var st = s.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, s, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, s, new object[] { Path.GetDirectoryName(target) });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, s, new object[] { "Glint Screenshot-Tool" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
        }

        public static void Install(bool startMenu, bool desktop, bool autostart)
        {
            Directory.CreateDirectory(InstallDir);
            if (!RunningInstalled) File.Copy(CurrentExe, InstalledExe, true);
            if (startMenu) { Directory.CreateDirectory(StartMenuDir); Shortcut(Path.Combine(StartMenuDir, "Glint.lnk"), InstalledExe); }
            if (desktop) { Directory.CreateDirectory(DesktopDir); Shortcut(Path.Combine(DesktopDir, "Glint.lnk"), InstalledExe); }
            SetAutostart(autostart, InstalledExe);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Glint");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("Publisher", "Glint");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            Log.Info("Installiert nach " + InstallDir);
        }

        public static void RemoveEntries()
        {
            SetAutostart(false);
            foreach (var d in new[] { StartMenuDir, DesktopDir })
            {
                string lnk = Path.Combine(d, "Glint.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        }

        public static void Uninstall()
        {
            if (MessageBox.Show(L.T("Glint vom Computer entfernen?"), "Glint", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            bool wipe = MessageBox.Show(L.T("Auch Einstellungen, gespeicherte Upload-Zugangsdaten und Logs löschen?\nDeine Screenshots bleiben erhalten."), "Glint", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            StopRunning();
            RemoveEntries();
            if (wipe && Directory.Exists(Settings.Dir)) Directory.Delete(Settings.Dir, true);
            // A running exe can't delete itself.
            string cmd = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
