using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyCompany("moolean")]
[assembly: System.Reflection.AssemblyProduct("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]

namespace TaskbarSystemMonitor
{
    internal static class Program
    {
        internal const string MutexName = "Local\\TaskbarSystemMonitor.moolean";
        internal const string ExitEvent = "Local\\TaskbarSystemMonitor.moolean.ExitV2";
        internal const string ShowEvent = "Local\\TaskbarSystemMonitor.moolean.ShowV2";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--exit") >= 0) return Signal(ExitEvent);
            if (Array.IndexOf(args, "--self-test") >= 0)
                return SelfTests.Run(args);
            string desktopReport = Array.Find(args, x => x.StartsWith("--appbar-test=", StringComparison.OrdinalIgnoreCase));

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) { if (desktopReport != null) return 21; Signal(ShowEvent); return 0; }
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
