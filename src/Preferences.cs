using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;

namespace TaskbarSystemMonitor
{
    internal sealed class Settings
    {
        internal bool Dock = true, SoftBackground = true, FirstRun = true;
        internal int Height = 24, FontSize = 9, Interval = 1000;
        internal string Theme = "Auto", Alignment = "Right", NetworkId = "";
        internal List<string> Items = new List<string> { "Cpu", "Memory", "Download", "Upload" };
        internal static readonly string[] MetricIds = { "Cpu", "Memory", "MemoryUsed", "Download", "Upload", "Battery", "Clock" };
        internal static readonly string[] MetricNames = { "CPU 使用率", "内存使用率", "已用 / 总内存", "下载速度", "上传速度", "电池电量", "当前时间" };
        internal Settings Copy() { var s = (Settings)MemberwiseClone(); s.Items = new List<string>(Items); return s; }
        internal void Validate()
        {
            Height = Height == 28 || Height == 32 ? Height : 24;
            FontSize = Math.Max(8, Math.Min(11, FontSize));
            Interval = Interval == 2000 || Interval == 5000 ? Interval : 1000;
            if (Theme != "Light" && Theme != "Dark") Theme = "Auto";
            if (Alignment != "Left" && Alignment != "Center") Alignment = "Right";
            Items = Items.Where(x => Array.IndexOf(MetricIds, x) >= 0).Distinct().ToList();
            if (Items.Count == 0) Items.Add("Cpu");
        }
        internal static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarSystemMonitor", "settings.xml"); } }
        internal static Settings Load(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            try
            {
                XElement root = XElement.Load(path);
                s.Dock = (bool?)root.Attribute("dock") ?? true;
                s.FirstRun = (bool?)root.Attribute("firstRun") ?? true;
                s.SoftBackground = (bool?)root.Attribute("soft") ?? true;
                s.Height = (int?)root.Attribute("height") ?? 24;
                s.FontSize = (int?)root.Attribute("fontSize") ?? 9;
                s.Interval = (int?)root.Attribute("interval") ?? 1000;
                s.Theme = (string)root.Attribute("theme") ?? "Auto";
                s.Alignment = (string)root.Attribute("alignment") ?? "Right";
                s.NetworkId = (string)root.Attribute("network") ?? "";
                s.Items = root.Elements("item").Select(x => (string)x).ToList();
                s.Validate();
            }
            catch (Exception error) { Log.Error(error); return new Settings(); }
            return s;
        }
        internal void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var root = new XElement("settings", new XAttribute("dock", Dock), new XAttribute("firstRun", FirstRun), new XAttribute("soft", SoftBackground),
                new XAttribute("height", Height), new XAttribute("fontSize", FontSize), new XAttribute("interval", Interval),
                new XAttribute("theme", Theme), new XAttribute("alignment", Alignment), new XAttribute("network", NetworkId),
                Items.Select(x => new XElement("item", x)));
            string temp = path + ".tmp";
            root.Save(temp);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }

    internal static class Startup
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool Enabled
        {
            get
            {
                try { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && string.Equals(key.GetValue("TaskbarSystemMonitor") as string, Command, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            }
        }
        internal static string Command { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }
        internal static void Set(bool enable)
        {
            // Failure is reported to the caller; a saved preference is not a startup registration.
            using (var key = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (enable) key.SetValue("TaskbarSystemMonitor", Command);
                else key.DeleteValue("TaskbarSystemMonitor", false);
            }
        }
    }

    internal sealed class Palette
    {
        internal Color Background, Surface, Text, Muted, Border, Cpu, Memory;
        internal static Palette Current() { return Current("Auto"); }
        internal static Palette Current(string theme)
        {
            if (SystemInformation.HighContrast)
                return new Palette { Background = SystemColors.Window, Surface = SystemColors.Window, Text = SystemColors.WindowText, Muted = SystemColors.WindowText, Border = SystemColors.WindowText, Cpu = SystemColors.Highlight, Memory = SystemColors.Highlight };
            bool light = theme != "Dark";
            if (theme == "Auto")
            {
                try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (key != null) light = Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 1)) != 0; }
                catch { }
            }
            return Create(light);
        }
        internal static Palette Create(bool light)
        {
            return light ? new Palette { Background = Color.FromArgb(237,240,244), Surface = Color.FromArgb(249,250,252), Text = Color.FromArgb(31,39,49), Muted = Color.FromArgb(91,103,118), Border = Color.FromArgb(213,220,229), Cpu = Color.FromArgb(0,130,173), Memory = Color.FromArgb(129,86,194) }
                : new Palette { Background = Color.FromArgb(26,29,35), Surface = Color.FromArgb(34,39,47), Text = Color.FromArgb(234,239,246), Muted = Color.FromArgb(156,169,188), Border = Color.FromArgb(53,62,76), Cpu = Color.FromArgb(79,194,227), Memory = Color.FromArgb(187,150,239) };
        }
    }
}
