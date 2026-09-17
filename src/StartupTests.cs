using System;
using System.Linq;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class StartupTests
    {
        internal static void Run()
        {
            const string sid = "S-1-5-21-101-202-303-1001";
            const string path = @"C:\Program Files\A & B\TaskbarSystemMonitor.exe";
            XNamespace ns = Startup.Schema;
            foreach (bool enabled in new[] { true, false })
            {
                string xml = Startup.Definition(path, sid, enabled); var root = XElement.Parse(xml);
                Require(Startup.IsOwned(xml, sid) && !Startup.IsOwned(xml, sid + "0"), "Task ownership is per user");
                var trigger = root.Element(ns + "Triggers").Element(ns + "LogonTrigger");
                Require((bool)trigger.Element(ns + "Enabled") == enabled && (string)trigger.Element(ns + "UserId") == sid, "Logon switch is explicit and user-scoped");
                var principal = root.Element(ns + "Principals").Element(ns + "Principal");
                Require((string)principal.Element(ns + "LogonType") == "InteractiveToken" && (string)principal.Element(ns + "RunLevel") == "LeastPrivilege", "No stored password, elevation or service desktop");
                var options = root.Element(ns + "Settings");
                Require((string)options.Element(ns + "ExecutionTimeLimit") == "PT0S", "No default 72-hour runtime cutoff");
                Require(!(bool)options.Element(ns + "DisallowStartIfOnBatteries") && !(bool)options.Element(ns + "StopIfGoingOnBatteries"), "Battery use must not stop the monitor");
                Require(!(bool)options.Element(ns + "RunOnlyIfNetworkAvailable") && !(bool)options.Element(ns + "RunOnlyIfIdle"), "No network or idle gate");
                Require(!(bool)options.Element(ns + "AllowHardTerminate"), "Scheduler must not discard notes on manual task stop");
                Require((string)options.Element(ns + "MultipleInstancesPolicy") == "IgnoreNew" && (bool)options.Element(ns + "Enabled"), "One running task; disabling logon does not prevent manual launch");
                Require((int)options.Element(ns + "RestartOnFailure").Element(ns + "Count") == 3 && (string)options.Element(ns + "RestartOnFailure").Element(ns + "Interval") == "PT1M", "Bounded failure retry policy");
                var action = root.Element(ns + "Actions").Element(ns + "Exec");
                Require((string)action.Element(ns + "Command") == path && (string)action.Element(ns + "Arguments") == "--startup", "Direct EXE action and XML-safe paths");
                Require(!xml.Contains("powershell") && !root.Descendants(ns + "Password").Any(), "No resident wrapper or credentials");
            }
            Require(Startup.NameFor(sid) == Startup.NameFor(sid) && Startup.NameFor(sid) != Startup.NameFor(sid + "0"), "Stable distinct task names");
            Require(!Startup.IsOwned("<Task/>", sid), "An unrelated task is rejected safely");
            bool rejected = false; try { Startup.Definition("relative.exe", sid, true); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Relative and unrelated executable paths are rejected");
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monitor-migration-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            string source = System.IO.Path.Combine(folder, "legacy.xml"), destination = System.IO.Path.Combine(folder, "settings.xml");
            try
            {
                const string original = "<settings><workItems><work keyword='Keep'><notes>  complete note\n</notes></work></workItems></settings>";
                System.IO.File.WriteAllText(source, original); AppPaths.CopySettingsOnce(source, destination);
                Require(System.IO.File.ReadAllText(destination) == original && System.IO.File.Exists(source), "Legacy migration preserves exact contents and the old backup");
                System.IO.File.WriteAllText(source, "older data"); AppPaths.CopySettingsOnce(source, destination);
                Require(System.IO.File.ReadAllText(destination) == original, "Migration cannot overwrite newer settings");
            }
            finally { if (System.IO.File.Exists(source)) System.IO.File.Delete(source); if (System.IO.File.Exists(destination)) System.IO.File.Delete(destination); System.IO.Directory.Delete(folder); }
        }
        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException("Startup test: " + reason); }
    }
}
