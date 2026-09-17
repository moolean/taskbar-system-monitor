using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class InfoTests
    {
        internal static void Run()
        {
            StartupTests.Run();
            Require(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName == ".NETFramework,Version=v4.8", "Published runtime must target .NET Framework 4.8");
            Require(System.Net.ServicePointManager.SecurityProtocol == System.Net.SecurityProtocolType.SystemDefault, "HTTPS must use OS-default TLS");
            Require(System.Net.ServicePointManager.ServerCertificateValidationCallback == null, "Certificate validation must not be bypassed");
            var quota = CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":10080,\"resetsAt\":1789823306},\"secondary\":null}}}"));
            Require(quota.Short == "Week 90% left" && quota.Available, "Quota interpretation");
            Require(!CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":null}}")).Available, "Missing quota is not zero usage");
            string path = Path.Combine(Path.GetTempPath(), "briefing-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var settings = new Settings { Accent = "Plum", GeoEnabled = false, AlwaysOnTop = true };
                settings.WorkItems.Add(new WorkItem { Keyword = "Test project", Notes = "Line 1\r\nLine 2", Link = "https://example.com" });
                settings.Save(path); var loaded = Settings.Load(path);
                Require(loaded.WorkItems.Count == 1 && loaded.WorkItems[0].Keyword == "Test project" && loaded.WorkItems[0].Notes.Contains("Line 2") && loaded.Accent == "Plum" && !loaded.GeoEnabled, "Work and connection persistence");
                var copy = loaded.Copy(); copy.WorkItems[0].Keyword = "changed"; Require(loaded.WorkItems[0].Keyword != "changed", "Isolated settings edits");
                Require(XElement.Load(path).Element("calendar") == null, "New settings do not write calendar credentials");
                Require(loaded.AlwaysOnTop && !new Settings().AlwaysOnTop, "Topmost opt-in persistence and disabled default");
                loaded.AlwaysOnTop = false; loaded.Save(path); Require(!Settings.Load(path).AlwaysOnTop, "Topmost disabled survives restart");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            TestCalendarRemoval();
            WorkTests.Run();
            TestRedirects();
            TestReadOnlyText();
            TestEnglishBar();
            Require(!ModulePopup.IsWebLink("file:///C:/Windows/System32/cmd.exe") && !ModulePopup.IsWebLink("https://user:password@example.com") && ModulePopup.IsWebLink("https://example.com/a"), "Safe links");
            using (var settingsUi = new SettingsForm(new Settings { FirstRun = false })) Require(!settingsUi.Value.AlwaysOnTop, "Settings constructs with non-topmost default");
            using (var bitmap = new Bitmap(2048, 24)) using (var g = Graphics.FromImage(bitmap))
            {
                int hidden; var cells = BarRenderer.Layout(g, new Rectangle(0, 0, 2048, 24), new Settings(), Demo(), 96, out hidden);
                Require(hidden == 0 && cells.Last().Bounds.Right >= 2048 - 40 && cells.First().Bounds.Left <= 16, "Full width is used");
                Require(cells.First(x => x.Id == "Tracks").Bounds.Width > 230, "Work summary expands into unused space");
                Require(cells.All(x => x.Id != "Calendar"), "Retired calendar cannot consume bar space");
            }
        }
        private static void TestReadOnlyText()
        {
            Require(BarRenderer.IsReadOnly("Codex") && BarRenderer.IsReadOnly("Ip") && !BarRenderer.IsReadOnly("Tracks") && !BarRenderer.IsReadOnly("Cpu"), "Only Codex and IP become passive text");
            foreach (bool light in new[] { true, false })
            foreach (string id in new[] { "Codex", "Ip" })
            using (var normal = new Bitmap(320, 28)) using (var hover = new Bitmap(320, 28))
            using (var g = Graphics.FromImage(normal)) using (var h = Graphics.FromImage(hover))
            {
                var settings = new Settings { SoftBackground = false, Alignment = "Left", Items = new System.Collections.Generic.List<string> { id } };
                var palette = Palette.Create(light); var bounds = new Rectangle(0, 0, 320, 28);
                BarRenderer.Draw(g, bounds, settings, Demo(), palette, 96);
                BarRenderer.Draw(h, bounds, settings, Demo(), palette, 96, id);
                int hidden; var cell = BarRenderer.Layout(g, bounds, settings, Demo(), 96, out hidden).Single();
                Require(normal.GetPixel(cell.Bounds.Left + 3, 14).ToArgb() == palette.Background.ToArgb(), "Read-only text has no button background");
                for (int y = 0; y < normal.Height; y++) for (int x = 0; x < normal.Width; x++)
                    Require(normal.GetPixel(x, y) == hover.GetPixel(x, y), "Read-only text has no hover highlight");
            }
        }
        private static void TestEnglishBar()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach (string locale in new[] { "zh-CN", "fr-FR", "ar-SA" })
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(locale);
                    var sample = Demo();
                    Require(sample.Value("Clock") == "09/16 Wed 14:30", "Clock always uses English weekday and Gregorian date");
                    Require(sample.MemoryDetail == "13.4 / 32.0 GiB" && Snapshot.Speed(1536) == "1.5 MiB/s", "Bar numbers use consistent decimal points");
                    Require(new BriefingData().Codex.Short == "Connecting…" && new BriefingData().WorkSummary == "Add keyword…", "Empty bar states are English");
                    foreach (string id in Settings.MetricIds) Require(BarRenderer.Label(id).All(c => c < 128 || c == '↓' || c == '↑'), "Built-in bar labels are English");
                    Require(sample.Briefing.WorkKeywords[0] == "项目交付", "User keyword text is never translated");
                    var quota = CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":23,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":10,\"windowDurationMins\":10080}}}"));
                    Require(quota.Short == "5h 77% left · Week 90% left", "Quota windows use English labels");
                    Require(CodexQuota.Parse(JsonData.Parse("{}")).Short == "Unavailable", "Missing quota has an English state");
                }
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = previous; }
        }
        private static void TestCalendarRemoval()
        {
            Require(!Settings.MetricIds.Contains("Calendar") && !new Settings().Items.Contains("Calendar"), "No calendar in module registry or defaults");
            Require(!Program.Actions.Contains("Calendar") && Program.RequestedAction(new[] { "--module=Calendar" }) == null, "No calendar action entry point");
            Require(Settings.MetricIds.Length == Settings.MetricNames.Length, "Module names match IDs");
            string path = Path.Combine(Path.GetTempPath(), "calendar-removal-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Require(!Settings.RemoveLegacyCalendar(path), "Missing old settings need no cleanup");
                var original = new XElement("settings", new XAttribute("version", 3), new XAttribute("alwaysOnTop", false), new XAttribute("geoEnabled", false), new XAttribute("fontSize", 10), new XAttribute("accent", "Jade"),
                    new XElement("item", "Tracks"), new XElement("item", "Calendar"), new XElement("item", "Cpu"),
                    new XElement("calendar", new XAttribute("url", "https://example.com"), new XAttribute("user", "test-only-account"), new XElement("secret", "test-only-ciphertext")),
                    new XElement("codexPath", "C:\\example\\codex.exe"), new XElement("futureSetting", "preserve-me"),
                    new XElement("workItems", new WorkItem { Keyword = "Test project", Notes = "Keep my notes" }.Save()));
                original.Save(path);
                Require(!Settings.Load(path).Items.Contains("Calendar"), "Even an uncleaned old file cannot enable calendar");
                Require(Settings.RemoveLegacyCalendar(path), "Old credentials are removed");
                var expected = new XElement(original); expected.Elements("calendar").Remove(); expected.Elements("item").Where(x => (string)x == "Calendar").Remove();
                Require(XNode.DeepEquals(expected, XElement.Load(path)), "Cleanup preserves all unrelated XML, including unknown settings");
                string cleaned = File.ReadAllText(path);
                Require(!cleaned.Contains("test-only-account") && !cleaned.Contains("test-only-ciphertext"), "Account and encrypted secret are both gone");
                Require(!Settings.RemoveLegacyCalendar(path) && File.ReadAllText(path) == cleaned, "Cleanup is idempotent");
                var loaded = Settings.Load(path);
                Require(loaded.Items.SequenceEqual(new[] { "Tracks", "Cpu" }) && loaded.WorkItems.Single().Notes == "Keep my notes" && loaded.FontSize == 10 && loaded.Accent == "Jade" && !loaded.AlwaysOnTop && !loaded.GeoEnabled && loaded.CodexPath == "C:\\example\\codex.exe", "User preferences survive upgrade");
                loaded.Save(path); Require(XElement.Load(path).Element("calendar") == null, "Later saves cannot recreate credentials");
                new XElement("settings", new XAttribute("version", 2), new XElement("item", "Calendar")).Save(path);
                Settings.RemoveLegacyCalendar(path);
                loaded = Settings.Load(path); Require(loaded.Items.Count > 0 && !loaded.Items.Contains("Calendar"), "Version 2 migration cannot restore calendar");
                new XElement("settings", new XAttribute("version", 3), new XElement("item", "Calendar")).Save(path);
                Settings.RemoveLegacyCalendar(path); Require(Settings.Load(path).Items.SequenceEqual(new[] { "Cpu" }), "Calendar-only configuration has a usable fallback");
                File.WriteAllText(path, "<settings>");
                bool rejected = false; try { Settings.RemoveLegacyCalendar(path); } catch (System.Xml.XmlException) { rejected = true; }
                Require(rejected && File.ReadAllText(path) == "<settings>", "Malformed configuration is not overwritten");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void TestRedirects()
        {
            var origin = new Uri("https://example.com/data");
            Require(SafeHttp.RedirectTarget(origin, "/data/").AbsolutePath == "/data/", "Same-origin redirect");
            foreach (string target in new[] { "https://elsewhere.example/data", "http://example.com/data", "https://example.com:444/data", "https://user:password@example.com/data" })
            {
                bool rejected = false; try { SafeHttp.RedirectTarget(origin, target); } catch (InvalidOperationException) { rejected = true; }
                Require(rejected, "Unsafe redirect is rejected");
            }
        }
        internal static Snapshot Demo()
        {
            return new Snapshot { Cpu = 12, Memory = 42, UsedBytes = 14431090114, TotalBytes = 34359738368, RxKbps = 2355, TxKbps = 86, Time = new DateTime(2026, 9, 16, 14, 30, 0),
                Briefing = new BriefingData { Codex = new Reading("Week 90% left", "示例", true), Location = new Reading("203.0.113.8 · Example City", "示例", true), WorkSummary = "项目交付 · 数据复核", WorkKeywords = new System.Collections.Generic.List<string> { "项目交付", "数据复核" } } };
        }
        internal static void Preview(string path)
        {
            using (var bitmap = new Bitmap(2048, 64)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                foreach (bool light in new[] { true, false })
                {
                    var settings = new Settings { Theme = light ? "Light" : "Dark", Accent = "Ocean" };
                    var sample = Demo();
                    sample.Briefing.WorkKeywords.AddRange(new[] { "发布计划", "客户同步", "方案评审", "版本回归", "阅读清单", "性能优化", "文档整理", "下周安排" });
                    BarRenderer.Draw(graphics, new Rectangle(0, light ? 0 : 36, 2048, 28), settings, sample, Palette.Current(settings), 96);
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Info test failed: " + message); }
    }
}
