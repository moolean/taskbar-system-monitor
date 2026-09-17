using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyCompany("moolean")]
[assembly: System.Reflection.AssemblyProduct("Taskbar System Monitor")]
[assembly: System.Reflection.AssemblyVersion("3.7.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.7.0.0")]

namespace TaskbarSystemMonitor
{
    internal static class Program
    {
        internal const string MutexName = "Local\\TaskbarSystemMonitor.moolean";
        internal const string ExitEvent = "Local\\TaskbarSystemMonitor.moolean.ExitV2";
        internal const string ShowEvent = "Local\\TaskbarSystemMonitor.moolean.ShowV2";
        internal const string ActionEvent = "Local\\TaskbarSystemMonitor.moolean.Action.";
        internal static readonly string[] Actions = { "Codex", "Ip", "Tracks", "Settings" };
        internal static string RequestedAction(string[] args)
        { string action = Array.Find(args, x => x.StartsWith("--module=", StringComparison.Ordinal)); return action != null && Array.IndexOf(Actions, action.Substring(9)) >= 0 ? action.Substring(9) : null; }

        [STAThread]
        private static int Main(string[] args)
        {
            string startupAction = Array.Find(args, x => x.StartsWith("--startup-control=", StringComparison.Ordinal));
            if (startupAction != null)
            {
                string report = Array.Find(args, x => x.StartsWith("--startup-report=", StringComparison.Ordinal));
                return Startup.Control(startupAction.Substring(18), report == null ? null : report.Substring(17));
            }
            if (Array.IndexOf(args, "--exit") >= 0) return Signal(ExitEvent);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--self-test") >= 0)
                return SelfTests.Run(args);
            string sourceReport = Array.Find(args, x => x.StartsWith("--codex-test=", StringComparison.OrdinalIgnoreCase));
            if (sourceReport != null)
            {
                try { Reading quota = CodexQuota.Read(""); File.WriteAllText(sourceReport.Substring(13), quota.Short + "\r\n" + quota.FullDetails); return quota.Available ? 0 : 22; }
                catch (Exception error) { File.WriteAllText(sourceReport.Substring(13), InfoHub.SafeError(error)); return 22; }
            }
            string desktopReport = Array.Find(args, x => x.StartsWith("--appbar-test=", StringComparison.OrdinalIgnoreCase));
            string workReport = Array.Find(args, x => x.StartsWith("--work-test=", StringComparison.OrdinalIgnoreCase));
            string barSample = Array.Find(args, x => x.StartsWith("--bar-sample=", StringComparison.OrdinalIgnoreCase));
            if (barSample != null) return WorkTests.BarSample(barSample.Substring(13), Array.IndexOf(args, "--dark") >= 0);
            if (workReport != null)
            {
                try
                {
                    var demo = WorkTests.Demo(); if (Array.IndexOf(args, "--dark") >= 0) demo.Theme = "Dark";
                    using (var form = new WorkForm(demo, delegate { return true; }))
                    {
                        form.Text += " · 示例检查（不写入配置）";
                        // Keep inspection stable while developer tools take focus;
                        // the same non-topmost pin is available in the real panel.
                        form.KeepOpen = true;
                        form.ShowInTaskbar = true; // Expose only the sample to desktop inspection tools.
                        form.ShowDialog();
                    }
                    File.WriteAllText(workReport.Substring(12), "PASS: keyword workspace closed; no real configuration was written."); return 0;
                }
                catch (Exception error) { File.WriteAllText(workReport.Substring(12), error.ToString()); return 25; }
            }
            string settingsReport = Array.Find(args, x => x.StartsWith("--settings-test=", StringComparison.OrdinalIgnoreCase));
            if (settingsReport != null)
            {
                try
                {
                    bool workSample = Array.IndexOf(args, "--work-sample") >= 0;
                    using (var form = new SettingsForm(workSample ? WorkTests.Demo() : new Settings { FirstRun = false }, workSample ? "Tracks" : null))
                    {
                        File.WriteAllText(settingsReport.Substring(16), "Settings constructed; waiting for the dialog to close.");
                        form.ShowDialog();
                        File.WriteAllText(settingsReport.Substring(16), "PASS: settings dialog opened and closed without saving real configuration.");
                    }
                    return 0;
                }
                catch (Exception error) { File.WriteAllText(settingsReport.Substring(16), error.ToString()); return 24; }
            }

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
                string folder = AppPaths.DirectoryPath;
                Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "runtime.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine);
            }
            catch { }
        }
        internal static void Error(Exception error)
        {
            try
            {
                string folder = AppPaths.DirectoryPath;
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
