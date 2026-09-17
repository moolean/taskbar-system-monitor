using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;

namespace TaskbarSystemMonitor
{
    // One per-user scheduled task owns startup and the process lifetime. Neither
    // a resident PowerShell script nor the installer is the monitor's parent.
    internal static class Startup
    {
        private const string LegacyKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Description = "Taskbar System Monitor: per-user desktop monitor. Managed by the application.";
        internal static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        private static string UserSid { get { using (var user = WindowsIdentity.GetCurrent()) return user.User.Value; } }
        internal static string TaskName { get { return NameFor(UserSid); } }
        internal static string NameFor(string sid)
        {
            using (var hash = SHA256.Create())
                return "TaskbarSystemMonitor-" + string.Concat(hash.ComputeHash(Encoding.UTF8.GetBytes(sid)).Take(6).Select(x => x.ToString("x2")));
        }
        private static XElement Node(string name, object value) { return new XElement(Schema + name, value); }
        internal static string Definition(string executable, string sid, bool enabled)
        {
            if (!Path.IsPathRooted(executable) || Path.GetFileName(executable) != "TaskbarSystemMonitor.exe") throw new ArgumentException("Expected an absolute monitor executable path.");
            return new XElement(Schema + "Task", new XAttribute("version", "1.2"),
                Node("RegistrationInfo", new[] { Node("Author", "moolean"), Node("Description", Description) }),
                Node("Triggers", Node("LogonTrigger", new[] { Node("Enabled", enabled), Node("UserId", sid), Node("Delay", "PT5S") })),
                Node("Principals", new XElement(Schema + "Principal", new XAttribute("id", "CurrentUser"), Node("UserId", sid), Node("LogonType", "InteractiveToken"), Node("RunLevel", "LeastPrivilege"))),
                Node("Settings", new[] {
                    Node("MultipleInstancesPolicy", "IgnoreNew"), Node("DisallowStartIfOnBatteries", false), Node("StopIfGoingOnBatteries", false),
                    Node("AllowHardTerminate", false), Node("StartWhenAvailable", true), Node("RunOnlyIfNetworkAvailable", false),
                    Node("IdleSettings", new[] { Node("StopOnIdleEnd", false), Node("RestartOnIdle", false) }),
                    Node("AllowStartOnDemand", true), Node("Enabled", true), Node("Hidden", false), Node("RunOnlyIfIdle", false),
                    Node("WakeToRun", false), Node("ExecutionTimeLimit", "PT0S"), Node("Priority", 7),
                    Node("RestartOnFailure", new[] { Node("Interval", "PT1M"), Node("Count", 3) }) }),
                new XElement(Schema + "Actions", new XAttribute("Context", "CurrentUser"),
                    Node("Exec", new[] { Node("Command", executable), Node("Arguments", "--startup"), Node("WorkingDirectory", Path.GetDirectoryName(executable)) })))
                .ToString(SaveOptions.DisableFormatting);
        }
        internal static bool IsOwned(string xml, string sid)
        {
            var root = XElement.Parse(xml);
            var registration = root.Element(Schema + "RegistrationInfo");
            var principals = root.Element(Schema + "Principals");
            var principal = principals == null ? null : principals.Element(Schema + "Principal");
            return registration != null && principal != null
                && (string)registration.Element(Schema + "Description") == Description
                && (string)principal.Element(Schema + "UserId") == sid;
        }
        private static void RequireOwned(dynamic task)
        {
            if (task != null && !IsOwned((string)task.Xml, UserSid)) throw new InvalidOperationException("A different task uses this name; it was not changed.");
        }
        private static dynamic Connect()
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            try { service.Connect(); return service; } catch { Release(service); throw; }
        }
        private static dynamic GetTask(dynamic folder)
        {
            try { return folder.GetTask(TaskName); }
            // COM interop may translate this HRESULT into FileNotFoundException
            // instead of COMException, depending on the installed runtime.
            catch (Exception error) { if (error.HResult == unchecked((int)0x80070002)) return null; throw; }
        }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
        internal static bool Enabled
        {
            get
            {
                try { var state = Inspect(); return state.Registered ? state.AutoStart : LegacyEnabled; }
                catch { return LegacyEnabled; }
            }
        }
        private static bool LegacyEnabled
        {
            get { try { using (var key = Registry.CurrentUser.OpenSubKey(LegacyKey)) return key != null && key.GetValue("TaskbarSystemMonitor") is string; } catch { return false; } }
        }
        private static void RemoveLegacy()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(LegacyKey, true))
                if (key != null) key.DeleteValue("TaskbarSystemMonitor", false);
        }
        internal static void Set(bool enabled)
        {
            dynamic service = null, folder = null, existing = null, registered = null;
            try
            {
                service = Connect(); folder = service.GetFolder("\\"); existing = GetTask(folder); RequireOwned(existing);
                if (enabled || existing != null)
                    registered = folder.RegisterTask(TaskName, Definition(Application.ExecutablePath, UserSid, enabled), 6, UserSid, null, 3, null);
                // Keep legacy startup intact until task registration succeeds.
                RemoveLegacy();
            }
            finally { Release(registered); Release(existing); Release(folder); Release(service); }
        }
        internal static void Start()
        {
            dynamic service = null, folder = null, task = null, running = null;
            try
            {
                service = Connect(); folder = service.GetFolder("\\"); task = GetTask(folder);
                if (task == null) throw new InvalidOperationException("Run install.ps1 to register the independent launcher first.");
                RequireOwned(task); running = task.Run(null);
            }
            finally { Release(running); Release(task); Release(folder); Release(service); }
        }
        internal static void Remove()
        {
            dynamic service = null, folder = null, task = null;
            try
            {
                service = Connect(); folder = service.GetFolder("\\"); task = GetTask(folder); RequireOwned(task);
                if (task != null) folder.DeleteTask(TaskName, 0);
                RemoveLegacy();
            }
            finally { Release(task); Release(folder); Release(service); }
        }
        internal static StartupStatus Inspect()
        {
            dynamic service = null, folder = null, task = null;
            try
            {
                service = Connect(); folder = service.GetFolder("\\"); task = GetTask(folder);
                var result = new StartupStatus { TaskName = TaskName, LegacyRunEntry = LegacyEnabled };
                if (task == null) return result;
                RequireOwned(task); var root = XElement.Parse((string)task.Xml); var options = root.Element(Schema + "Settings");
                result.Registered = true; result.State = (int)task.State; result.LastResult = (int)task.LastTaskResult;
                result.AutoStart = (bool)task.Enabled && root.Element(Schema + "Triggers").Elements(Schema + "LogonTrigger").Any(x => (bool?)x.Element(Schema + "Enabled") != false);
                result.Executable = (string)root.Element(Schema + "Actions").Element(Schema + "Exec").Element(Schema + "Command");
                result.ExecutionTimeLimit = (string)options.Element(Schema + "ExecutionTimeLimit");
                result.StopOnBattery = (bool?)options.Element(Schema + "StopIfGoingOnBatteries") ?? true;
                var retry = options.Element(Schema + "RestartOnFailure");
                result.RestartCount = retry == null ? 0 : (int?)retry.Element(Schema + "Count") ?? 0;
                return result;
            }
            finally { Release(task); Release(folder); Release(service); }
        }
        internal static int Control(string action, string report)
        {
            try
            {
                switch (action) { case "enable": Set(true); break; case "disable": Set(false); break; case "start": Start(); break; case "remove": Remove(); break; case "status": break; default: throw new ArgumentException("Unknown startup action."); }
                if (report != null) File.WriteAllText(report, new JavaScriptSerializer().Serialize(Inspect()));
                return 0;
            }
            catch (Exception error)
            {
                if (report != null) File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { Error = error.Message }));
                return 26;
            }
        }
    }
    internal sealed class StartupStatus
    {
        public string TaskName, Executable, ExecutionTimeLimit;
        public bool Registered, AutoStart, StopOnBattery, LegacyRunEntry;
        public int State, LastResult, RestartCount;
    }
}
