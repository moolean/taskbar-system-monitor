using System;
using System.IO;

namespace TaskbarSystemMonitor
{
    internal static class AppPaths
    {
        // AppData writes made by packaged host applications may be redirected
        // into their private package cache. This user-owned location is shared
        // by Explorer, Task Scheduler, the installer and the standalone EXE.
        internal static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "TaskbarSystemMonitor"); } }
        internal static string SettingsPath { get { return Path.Combine(DirectoryPath, "settings.xml"); } }
        internal static void MigrateLegacySettings()
        {
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarSystemMonitor", "settings.xml");
            CopySettingsOnce(legacy, SettingsPath);
        }
        internal static void CopySettingsOnce(string source, string destination)
        {
            if (File.Exists(destination) || !File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, false); // Never overwrite existing notes.
        }
    }
}
