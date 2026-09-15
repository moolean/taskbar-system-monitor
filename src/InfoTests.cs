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
            Require(AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName == ".NETFramework,Version=v4.8", "Published runtime must target .NET Framework 4.8");
            Require(System.Net.ServicePointManager.SecurityProtocol == System.Net.SecurityProtocolType.SystemDefault, "HTTPS must use OS-default TLS, not legacy SSL3/TLS1.0");
            Require(System.Net.ServicePointManager.ServerCertificateValidationCallback == null, "Certificate validation must not be bypassed");
            Require(CalendarSource.Explain(new System.Net.WebException("test only", System.Net.WebExceptionStatus.SecureChannelFailure)).Contains("TLS"), "TLS failure is distinct from password failure");
            Require(CalendarSource.Explain(new System.Net.WebException("test only", System.Net.WebExceptionStatus.TrustFailure)).Contains("证书"), "Certificate failure is distinct from password failure");
            Require(CalendarSource.Explain(new System.Net.WebException("test only", System.Net.WebExceptionStatus.NameResolutionFailure)).Contains("DNS"), "DNS failure has an actionable explanation");
            var quota = CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":99}},\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":10,\"windowDurationMins\":10080,\"resetsAt\":1789823306},\"secondary\":null}}}"));
            Require(quota.Short == "周剩余 90%" && quota.Available, "Quota interpretation");
            Require(!CodexQuota.Parse(JsonData.Parse("{\"rateLimits\":{\"primary\":null}}" )).Available, "Missing quota is not zero usage");
            string secret = LocalSecret.Seal("test-password-not-real"); Require(LocalSecret.Open(secret) == "test-password-not-real" && !secret.Contains("test-password"), "DPAPI");
            string path = Path.Combine(Path.GetTempPath(), "briefing-test-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var settings = new Settings { CalendarSecret = secret, Accent = "Plum", GeoEnabled = false, AlwaysOnTop = true };
                settings.WorkItems.Add(new WorkItem { Title = "Test project", Status = "阻塞", Progress = 67, Notes = "Line 1\r\nLine 2", Link = "https://example.com" });
                settings.Save(path); var loaded = Settings.Load(path);
                Require(loaded.WorkItems.Count == 1 && loaded.WorkItems[0].Progress == 67 && loaded.WorkItems[0].Notes.Contains("Line 2") && loaded.Accent == "Plum" && !loaded.GeoEnabled && loaded.CalendarSecret == secret, "Work and connection persistence");
                var copy = loaded.Copy(); copy.WorkItems[0].Title = "changed"; Require(loaded.WorkItems[0].Title != "changed", "Isolated settings edits");
                Require(!File.ReadAllText(path).Contains("test-password-not-real"), "No plaintext secret in settings");
                Require(loaded.AlwaysOnTop && !new Settings().AlwaysOnTop, "Topmost opt-in persistence and disabled default");
                loaded.AlwaysOnTop = false; loaded.Save(path); Require(!Settings.Load(path).AlwaysOnTop, "Topmost disabled survives restart");
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
            TestCalendarConnection(secret);
            using (var settingsUi = new SettingsForm(new Settings { FirstRun = false })) Require(!settingsUi.Value.AlwaysOnTop, "Settings constructs with non-topmost default");
            var sample = Demo();
            using (var bitmap = new Bitmap(2048, 24)) using (var g = Graphics.FromImage(bitmap))
            {
                int hidden; var cells = BarRenderer.Layout(g, new Rectangle(0, 0, 2048, 24), new Settings(), sample, 96, out hidden);
                Require(hidden == 0 && cells.Last().Bounds.Right >= 2048 - 40 && cells.First().Bounds.Left <= 16, "Full width is used");
                var meeting = cells.First(x => x.Id == "Calendar"); Require(meeting.Bounds.Width > 230, "Meeting expands into unused space");
            }
        }
        private static void TestCalendarConnection(string secret)
        {
            Require(CalendarSource.ValidateUrl(" caldav.feishu.cn ").AbsoluteUri == "https://caldav.feishu.cn/", "Bare CalDAV host normalization");
            Require(CalendarSource.SameOrigin(new Uri("https://example.com/dav"), "/dav/").AbsolutePath == "/dav/", "Same-origin redirect target");
            var config = new Settings { CalendarUrl = "https://example.com/", CalendarUser = "test-user", CalendarSecret = secret };
            int requests = 0;
            var values = CalendarSource.Read(config, null, delegate(Uri uri, string method, string body, string auth, string depth)
            {
                requests++;
                Require(auth.StartsWith("Basic ") && uri.Host == "example.com", "Credentials remain with calendar origin");
                string response;
                if (requests == 1) response = "<d:response><d:propstat><d:prop><d:current-user-principal><d:href>/principals/u/</d:href></d:current-user-principal></d:prop></d:propstat></d:response>";
                else if (requests == 2) response = "<d:response><d:propstat><d:prop><c:calendar-home-set><d:href>calendars/</d:href></c:calendar-home-set></d:prop></d:propstat></d:response>";
                else if (requests == 3)
                {
                    Require(uri.AbsolutePath == "/principals/u/calendars/", "Calendar home relative to principal");
                    response = "<d:response><d:href>main/</d:href><d:propstat><d:prop><d:resourcetype><c:calendar/></d:resourcetype></d:prop></d:propstat></d:response>";
                }
                else
                {
                    Require(method == "REPORT" && uri.AbsolutePath == "/principals/u/calendars/main/", "Calendar relative to home");
                    string when = DateTime.UtcNow.AddHours(2).ToString("yyyyMMdd'T'HHmmss'Z'");
                    response = "<d:response><d:propstat><d:prop><c:calendar-data>BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:mock\nDTSTART:" + when + "\nDURATION:PT30M\nSUMMARY:Test only\nEND:VEVENT\nEND:VCALENDAR</c:calendar-data></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>";
                }
                return "<d:multistatus xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'>" + response + "</d:multistatus>";
            });
            Require(requests == 4 && values.Count == 1, "Read-only mocked CalDAV discovery and REPORT");
            bool explained = false;
            try { CalendarSource.Read(config, null, delegate { throw new InvalidOperationException(CalendarSource.HttpFailure(405)); }); }
            catch (InvalidOperationException error) { explained = error.Message.Contains("PROPFIND") && error.Message.Contains("405") && error.Message.Contains("不等同于密码错误"); }
            Require(explained, "Calendar error includes phase and protocol cause");
            bool denied = false;
            try { CalendarSource.CalendarData(XDocument.Parse("<d:multistatus xmlns:d='DAV:'><d:response><d:status>HTTP/1.1 403 Forbidden</d:status></d:response></d:multistatus>")).ToList(); }
            catch (InvalidOperationException error) { denied = error.Message.Contains("403"); }
            Require(denied, "DAV inner errors are not empty success");
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
