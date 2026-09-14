using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace TaskbarSystemMonitor
{
    internal static class InfoTests
    {
        internal static void Run()
        {
            var quota = CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":10080,\"resetsAt\":1789823306},\"secondary\":null}}}"));
            Require(quota.Short == "周剩余 90%" && quota.Available, "Quota interpretation");
            Require(!CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":null}}" )).Available, "Missing quota is not zero usage");
            string secret = LocalSecret.Seal("test-password-not-real"); Require(LocalSecret.Open(secret) == "test-password-not-real" && !secret.Contains("test-password"), "DPAPI");
            string path = Path.Combine(Path.GetTempPath(), "briefing-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var settings = new Settings { CalendarSecret = secret, Accent = "Plum", GeoEnabled = false };
                settings.WorkItems.Add(new WorkItem { Title = "Test project", Status = "阻塞", Progress = 67, Notes = "Line 1\r\nLine 2", Link = "https://example.com" });
                settings.Save(path); var loaded = Settings.Load(path);
                Require(loaded.WorkItems.Count == 1 && loaded.WorkItems[0].Progress == 67 && loaded.WorkItems[0].Notes.Contains("Line 2") && loaded.Accent == "Plum" && !loaded.GeoEnabled && loaded.CalendarSecret == secret, "Work and connection persistence");
                var copy = loaded.Copy(); copy.WorkItems[0].Title = "changed"; Require(loaded.WorkItems[0].Title != "changed", "Isolated settings edits");
                Require(!File.ReadAllText(path).Contains("test-password-not-real"), "No plaintext secret in settings");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            string calendar = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:one\r\nSUMMARY:Weekly "+"\r\n review\r\nDTSTART;TZID=Asia/Shanghai:20260914T170000\r\nDTEND:20260914T100000Z\r\nDESCRIPTION:one\\ntwo\\,three\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nUID:cancelled\r\nSTATUS:CANCELLED\r\nDTSTART:20260914T170000Z\r\nEND:VEVENT\r\nEND:VCALENDAR";
            var meetings = CalendarSource.ParseIcal(calendar);
            Require(meetings.Count == 1 && meetings[0].Title == "Weekly review" && meetings[0].Start.UtcDateTime.Hour == 9 && meetings[0].End.UtcDateTime.Hour == 10 && meetings[0].Notes == "one\ntwo,three", "ICS unfolding, cancellation, timezone");
            var duration = CalendarSource.ParseIcal("BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:duration\nDURATION:PT30M\nDTSTART:20260914T090000Z\nEND:VEVENT\nEND:VCALENDAR");
            Require((duration[0].End - duration[0].Start).TotalMinutes == 30, "Duration before DTSTART");
            bool blocked = false; try { CalendarSource.SameOrigin(new Uri("https://caldav.feishu.cn/"), "https://example.com/leak"); } catch (InvalidOperationException) { blocked = true; } Require(blocked, "Cross-origin credential protection");
            blocked = false; try { CalendarSource.ParseIcal(calendar.Replace("UID:one", "UID:one\r\nRRULE:FREQ=WEEKLY")); } catch (InvalidOperationException) { blocked = true; } Require(blocked, "Unexpanded recurrence is not silently ignored");
            Require(!ModulePopup.IsWebLink("file:///C:/Windows/System32/cmd.exe") && !ModulePopup.IsWebLink("https://user:password@example.com") && ModulePopup.IsWebLink("https://example.com/a"), "Safe links");
            var sample = Demo();
            using (var bitmap = new Bitmap(2048, 24)) using (var g = Graphics.FromImage(bitmap))
            {
                int hidden; var cells = BarRenderer.Layout(g, new Rectangle(0, 0, 2048, 24), new Settings(), sample, 96, out hidden);
                Require(hidden == 0 && cells.Last().Bounds.Right >= 2048 - 40 && cells.First().Bounds.Left <= 16, "Full width is used");
                var meeting = cells.First(x => x.Id == "Calendar"); Require(meeting.Bounds.Width > 230, "Meeting expands into unused space");
            }
        }
        internal static Snapshot Demo()
        {
            return new Snapshot { Cpu = 12, Memory = 42, UsedBytes = 14431090114, TotalBytes = 34359738368, RxKbps = 2355, TxKbps = 86, Time = new DateTime(2026, 9, 14, 14, 30, 0),
                Briefing = new BriefingData { Codex = new Reading("周剩余 90%", "示例", true), Calendar = new Reading("15:00 产品评审 · 30 分钟后", "示例", true), Location = new Reading("203.0.113.8 · 示例城市", "示例", true), WorkSummary = "项目交付 65% · 数据复核 30%" } };
        }
        internal static void Preview(string path)
        {
            using (var bitmap = new Bitmap(2048, 64)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.White);
                foreach (bool light in new[] { true, false })
                {
                    var settings = new Settings { Theme = light ? "Light" : "Dark", Accent = "Ocean" };
                    BarRenderer.Draw(graphics, new Rectangle(0, light ? 0 : 36, 2048, 28), settings, Demo(), Palette.Current(settings), 96);
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Info test failed: " + message); }
    }
}
