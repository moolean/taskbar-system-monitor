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
        internal bool Dock = true, SoftBackground = true, FirstRun = true, FillBar = true, TintTaskbar = true, GeoEnabled = false;
        internal bool AlwaysOnTop = false;
        internal int Height = 24, FontSize = 9, Interval = 1000;
        internal string Theme = "Auto", Alignment = "Right", NetworkId = "", Accent = "System", CodexPath = "";
        internal List<WorkItem> WorkItems = new List<WorkItem>();
        internal List<string> Items = new List<string> { "Cpu", "Memory", "Download", "Upload", "Codex", "Ip", "Tracks", "Clock" };
        internal static readonly string[] MetricIds = { "Cpu", "Memory", "MemoryUsed", "Download", "Upload", "Battery", "Codex", "Ip", "Tracks", "Clock" };
        internal static readonly string[] MetricNames = { "CPU 使用率", "内存使用率", "已用 / 总内存", "下载速度", "上传速度", "电池电量", "Codex 剩余额度", "IP / 大致位置", "工作追踪（可展开）", "日期与时间" };
        internal Settings Copy() { var s = (Settings)MemberwiseClone(); s.Items = new List<string>(Items); s.WorkItems = WorkItems.Select(x => x.Copy()).ToList(); return s; }
        internal void Validate()
        {
            Height = Height == 28 || Height == 32 ? Height : 24;
            FontSize = Math.Max(8, Math.Min(11, FontSize));
            Interval = Interval == 2000 || Interval == 5000 ? Interval : 1000;
            if (Theme != "Light" && Theme != "Dark") Theme = "Auto";
            if (Alignment != "Left" && Alignment != "Center") Alignment = "Right";
            if (Accent != "Ocean" && Accent != "Plum" && Accent != "Jade") Accent = "System";
            Items = Items.Where(x => Array.IndexOf(MetricIds, x) >= 0).Distinct().ToList();
            if (Items.Count == 0) Items.Add("Cpu");
            WorkItems = WorkItems.Where(x => !string.IsNullOrWhiteSpace(x.Keyword)).Take(30).ToList();
            foreach (var item in WorkItems) item.Validate();
        }
        internal static string DefaultPath { get { return AppPaths.SettingsPath; } }
        internal static bool MigrateWorkKeywords(string path)
        {
            if (!File.Exists(path)) return false;
            var root = XElement.Load(path); bool changed = false;
            foreach (var item in root.Elements("workItems").Elements("work"))
            {
                var title = item.Attribute("title");
                if (item.Attribute("keyword") == null && title != null) { item.SetAttributeValue("keyword", title.Value); changed = true; }
                foreach (string name in new[] { "title", "status", "progress" })
                { var old = item.Attribute(name); if (old != null) { old.Remove(); changed = true; } }
            }
            if (!changed) return false;
            string staged = path + ".cleanup-" + Guid.NewGuid().ToString("N");
            try { root.Save(staged); File.Replace(staged, path, null); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            return true;
        }
        internal static bool RemoveLegacyCalendar(string path)
        {
            if (!File.Exists(path)) return false;
            var root = XElement.Load(path);
            var removed = root.Elements("calendar").Concat(root.Elements("item").Where(x => (string)x == "Calendar")).ToList();
            if (removed.Count == 0) return false;
            foreach (var node in removed) node.Remove();
            // Only remove retired data; preserve every unrelated setting and do not
            // create a backup containing the old account or encrypted password.
            string staged = path + ".cleanup-" + Guid.NewGuid().ToString("N");
            try { root.Save(staged); File.Replace(staged, path, null); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            return true;
        }
        internal static Settings Load(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            try
            {
                XElement root = XElement.Load(path);
                s.Dock = (bool?)root.Attribute("dock") ?? true;
                s.AlwaysOnTop = (bool?)root.Attribute("alwaysOnTop") ?? false;
                s.FirstRun = (bool?)root.Attribute("firstRun") ?? true;
                s.SoftBackground = (bool?)root.Attribute("soft") ?? true;
                s.Height = (int?)root.Attribute("height") ?? 24;
                s.FontSize = (int?)root.Attribute("fontSize") ?? 9;
                s.Interval = (int?)root.Attribute("interval") ?? 1000;
                s.Theme = (string)root.Attribute("theme") ?? "Auto";
                s.Alignment = (string)root.Attribute("alignment") ?? "Right";
                s.NetworkId = (string)root.Attribute("network") ?? "";
                s.FillBar = (bool?)root.Attribute("fillBar") ?? true;
                s.TintTaskbar = (bool?)root.Attribute("tintTaskbar") ?? true;
                s.GeoEnabled = (bool?)root.Attribute("geoEnabled") ?? false;
                s.Accent = (string)root.Attribute("accent") ?? "System";
                s.CodexPath = (string)root.Element("codexPath") ?? "";
                var work = root.Element("workItems");
                if (work != null) s.WorkItems = work.Elements("work").Select(WorkItem.Load).ToList();
                s.Items = root.Elements("item").Select(x => (string)x).ToList();
                if (((int?)root.Attribute("version") ?? 2) < 3)
                    foreach (string id in new[] { "Codex", "Ip", "Tracks", "Clock" }) if (!s.Items.Contains(id)) s.Items.Add(id);
                s.Validate();
            }
            catch (Exception error) { Log.Error(error); return new Settings(); }
            return s;
        }
        internal void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var root = new XElement("settings", new XAttribute("version", 5), new XAttribute("dock", Dock), new XAttribute("alwaysOnTop", AlwaysOnTop), new XAttribute("firstRun", FirstRun), new XAttribute("soft", SoftBackground),
                new XAttribute("height", Height), new XAttribute("fontSize", FontSize), new XAttribute("interval", Interval),
                new XAttribute("theme", Theme), new XAttribute("alignment", Alignment), new XAttribute("network", NetworkId),
                new XAttribute("fillBar", FillBar), new XAttribute("tintTaskbar", TintTaskbar), new XAttribute("geoEnabled", GeoEnabled), new XAttribute("accent", Accent),
                Items.Select(x => new XElement("item", x)), new XElement("codexPath", CodexPath),
                new XElement("workItems", WorkItems.Select(x => x.Save())));
            string temp = path + ".tmp";
            root.Save(temp);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }

    internal sealed class Palette
    {
        internal Color Background, Surface, Text, Muted, Border, Cpu, Memory;
        internal static Palette Current() { return Current("Auto"); }
        internal static Palette Current(Settings settings)
        {
            Palette p = Current(settings.Theme);
            if (SystemInformation.HighContrast) return p;
            bool light = p.Background.GetBrightness() > 0.5f;
            Color accent = Color.FromArgb(52, 134, 178);
            if (settings.Accent == "Plum") accent = Color.FromArgb(142, 100, 186);
            else if (settings.Accent == "Jade") accent = Color.FromArgb(40, 150, 130);
            else if (settings.Accent == "System")
            {
                try { uint value; bool opaque; if (Native.DwmGetColorizationColor(out value, out opaque) == 0) accent = Color.FromArgb((int)((value >> 16) & 255), (int)((value >> 8) & 255), (int)(value & 255)); } catch { }
            }
            bool colorOnTaskbar = false;
            if (settings.Theme == "Auto" && settings.TintTaskbar)
                try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) colorOnTaskbar = key != null && Convert.ToInt32(key.GetValue("ColorPrevalence", 0)) != 0; } catch { }
            double strength = settings.TintTaskbar ? (colorOnTaskbar ? 0.26 : 0.08) : 0;
            p.Background = Blend(light ? Color.FromArgb(237, 240, 243) : Color.FromArgb(29, 32, 38), accent, strength);
            p.Surface = Blend(p.Background, light ? Color.White : Color.FromArgb(45, 48, 54), 0.25);
            p.Border = Blend(p.Background, accent, 0.3);
            p.Cpu = Blend(accent, light ? Color.Black : Color.White, light ? 0.20 : 0.4);
            return p;
        }
        internal static Color Blend(Color a, Color b, double amount) { return Color.FromArgb((int)(a.R * (1 - amount) + b.R * amount), (int)(a.G * (1 - amount) + b.G * amount), (int)(a.B * (1 - amount) + b.B * amount)); }
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
