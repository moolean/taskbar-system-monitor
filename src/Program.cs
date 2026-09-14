using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyCompany("moolean")]
[assembly: System.Reflection.AssemblyProduct("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.0.0.0")]

namespace TaskbarSystemMonitor
{
    internal static class Program
    {
        internal const string MutexName = "Local\\TaskbarSystemMonitor.moolean";
        internal const string ExitEvent = "Local\\TaskbarSystemMonitor.moolean.ExitV2";
        internal const string ShowEvent = "Local\\TaskbarSystemMonitor.moolean.ShowV2";
        internal const string ActionEvent = "Local\\TaskbarSystemMonitor.moolean.Action.";
        internal static readonly string[] Actions = { "Codex", "Calendar", "Ip", "Tracks", "Settings" };
        internal static string RequestedAction(string[] args)
        { string action = Array.Find(args, x => x.StartsWith("--module=", StringComparison.Ordinal)); return action != null && Array.IndexOf(Actions, action.Substring(9)) >= 0 ? action.Substring(9) : null; }

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--exit") >= 0) return Signal(ExitEvent);
            if (Array.IndexOf(args, "--self-test") >= 0)
                return SelfTests.Run(args);
            string sourceReport = Array.Find(args, x => x.StartsWith("--codex-test=", StringComparison.OrdinalIgnoreCase));
            if (sourceReport != null)
            {
                try { Reading quota = CodexQuota.Read(""); File.WriteAllText(sourceReport.Substring(13), quota.Short + "\r\n" + quota.FullDetails); return quota.Available ? 0 : 22; }
                catch (Exception error) { File.WriteAllText(sourceReport.Substring(13), InfoHub.SafeError(error)); return 22; }
            }
            string desktopReport = Array.Find(args, x => x.StartsWith("--appbar-test=", StringComparison.OrdinalIgnoreCase));

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) { if (desktopReport != null) return 21; string action = RequestedAction(args); Signal(action == null ? ShowEvent : ActionEvent + action); return 0; }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                try
                {
                    if (desktopReport != null) return DesktopTests.Run(desktopReport.Substring(14));
                    using (var context = new MonitorContext(args)) Application.Run(context);
                    return 0;
                }
                catch (Exception error)
                {
                    Log.Error(error);
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static int Signal(string name)
        {
            try { using (var signal = EventWaitHandle.OpenExisting(name)) signal.Set(); return 0; }
            catch (WaitHandleCannotBeOpenedException) { return 0; }
        }
    }

    internal static class Log
    {
        internal static void Info(string text)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarSystemMonitor");
                Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "runtime.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
            }
            catch { }
        }
        internal static void Error(Exception error)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarSystemMonitor");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 262144) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + error + Environment.NewLine);
            }
            catch
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "TaskbarSystemMonitor-error.log"), DateTime.Now.ToString("s") + " " + error + Environment.NewLine); }
                catch { /* Logging must not prevent shell cleanup. */ }
            }
        }
    }
}
