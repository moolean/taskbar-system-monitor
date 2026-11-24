using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class MouseBatteryTests
    {
        private const string ListJson = "{\"deviceInfos\":[{\"id\":\"dev01\",\"displayName\":\"Sample mouse\",\"deviceType\":\"MOUSE\",\"state\":\"ACTIVE\",\"capabilities\":{\"hasBatteryStatus\":true}},{\"id\":\"dev02\",\"displayName\":\"Keyboard\",\"deviceType\":\"KEYBOARD\",\"state\":\"ACTIVE\",\"capabilities\":{\"hasBatteryStatus\":true}}]}";
        internal static void Run()
        {
            var mouse = new LogitechMouse { Id = "dev01", Name = "Sample mouse", State = "ACTIVE" };
            foreach (int percent in new[] { 0, 5, 20, 73, 100 })
            foreach (bool charging in new[] { false, true })
            {
                var battery = JsonData.Parse("{\"deviceId\":\"dev01\",\"percentage\":" + percent + ",\"charging\":" + (charging ? "true" : "false") + "}");
                var result = LogitechBattery.ParseBattery(mouse, battery);
                Require(result.Available && result.Short == percent + "%" + (charging ? "+" : ""), "Percentage and charging; zero is valid only when explicitly reported");
            }
            foreach (string value in new[] { "null", "\"50\"", "true", "-1", "101" })
                Require(!LogitechBattery.ParseBattery(mouse, JsonData.Parse("{\"deviceId\":\"dev01\",\"percentage\":" + value + "}")).Available, "Reject missing, nonnumeric and out-of-range values");
            Require(!LogitechBattery.ParseBattery(mouse, JsonData.Parse("{\"deviceId\":\"wrong\",\"percentage\":80}")).Available, "Cross-device response rejected");
            foreach (string flag in new[] { "simpleBattery", "chargingError", "isBatteryRemoved" })
                Require(!LogitechBattery.ParseBattery(mouse, JsonData.Parse("{\"deviceId\":\"dev01\",\"percentage\":70,\"" + flag + "\":true}")).Available, "Invalid battery conditions cannot masquerade as a percentage");
            var old = new Reading("80%", "old", true) { UpdatedUtc = DateTime.UtcNow.AddMinutes(-2) };
            Require(LogitechBattery.Fresh(old).Short == "Stale" && !LogitechBattery.Fresh(old).Available, "Stale values not presented as current");

            var calls = new List<string>();
            Func<string, Dictionary<string, object>> query = delegate(string path)
            {
                calls.Add(path);
                return JsonData.Parse(path == "/devices/list" ? ListJson : "{\"deviceId\":\"dev01\",\"percentage\":73,\"charging\":false}");
            };
            var probe = LogitechBattery.ReadFrom(query, "");
            Require(probe.Devices.Count == 1 && probe.Reading.Short == "73%" && calls.SequenceEqual(new[] { "/devices/list", "/battery/dev01/state" }), "Only battery-capable mouse selected, GET paths limited");
            calls.Clear(); probe = LogitechBattery.ReadFrom(query, "Missing");
            Require(!probe.Reading.Available && calls.Count == 1, "A missing preferred mouse does not silently select another one");
            probe = LogitechBattery.ReadFrom(delegate(string p) { return JsonData.Parse(ListJson.Replace("ACTIVE", "INACTIVE")); }, "");
            Require(probe.Reading.Short == "Offline", "Sleeping mouse does not display cached battery");
            string multiple = ListJson.Replace("\"KEYBOARD\"", "\"MOUSE\"");
            probe = LogitechBattery.ReadFrom(delegate(string p) { return JsonData.Parse(multiple); }, "");
            Require(probe.Reading.Short == "Select mouse", "Ambiguous devices require a choice");
            Require(LogitechBattery.Devices(JsonData.Parse(ListJson.Replace("dev01", "../profile/remove"))).Count == 0, "Unsafe device identifiers rejected");
            foreach (string path in new[] { "/profile/remove", "/battery/../state", "http://example.com", "/battery/dev01/check" })
            {
                bool rejected = false; try { LogitechBattery.Request(path, "test"); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "Read-only endpoint allowlist");
            }
            Require(JsonData.Text(JsonData.Parse(LogitechBattery.Request("/devices/list", "test")), "verb") == "GET", "Requests never mutate devices");
            TestTransport();
            TestSettings();
            TestLayout();
        }
        private static void TestTransport()
        {
            using (var fake = new FakeSocket())
            using (var deadline = new CancellationTokenSource(1500))
            {
                var payload = LogitechBattery.Query(fake, "/devices/list", deadline.Token);
                Require(LogitechBattery.Devices(payload).Single().Name == "Sample mouse", "Fragmented response, UTF-8 decoding and unsolicited events");
            }
            foreach (string fault in new[] { "binary", "closed", "large", "flood", "timeout" })
            using (var fake = new FakeSocket { Fault = fault })
            using (var deadline = new CancellationTokenSource(100))
            {
                bool failed = false;
                try { LogitechBattery.Query(fake, "/devices/list", deadline.Token); }
                catch (InvalidDataException) { failed = true; }
                catch (OperationCanceledException) { failed = true; }
                Require(failed, "Bounded transport failure: " + fault);
            }
        }
        private static void TestSettings()
        {
            string path = Path.Combine(Path.GetTempPath(), "mouse-settings-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var source = XElement.Parse("<settings version='5' alwaysOnTop='false'><item>Cpu</item><item>Codex</item><item>Tracks</item><future>keep</future><workItems><work keyword='Example'><notes>  note\nnext</notes><link>https://example.com</link></work></workItems></settings>");
                source.Save(path); Require(Settings.EnableMouseBattery(path), "Explicit enable inserts module");
                var expected = new XElement(source); expected.Elements("item").First(x => (string)x == "Codex").AddBeforeSelf(new XElement("item", "MouseBattery"));
                Require(XNode.DeepEquals(expected, XElement.Load(path)), "Enable preserves notes, links, unknown settings and order");
                string once = File.ReadAllText(path);
                Require(!Settings.EnableMouseBattery(path) && File.ReadAllText(path) == once, "Enable idempotence");
                var settings = Settings.Load(path); settings.MouseName = "Sample mouse"; settings.Save(path);
                var loaded = Settings.Load(path);
                Require(loaded.MouseName == "Sample mouse" && loaded.Items.Contains("MouseBattery") && !loaded.AlwaysOnTop, "Selected device and visibility survive restart");
                loaded.Items.Remove("MouseBattery"); loaded.Save(path); Require(!Settings.Load(path).Items.Contains("MouseBattery"), "User can disable module");
                File.WriteAllText(path, "<settings>"); bool rejected = false;
                try { Settings.EnableMouseBattery(path); } catch (System.Xml.XmlException) { rejected = true; }
                Require(rejected && File.ReadAllText(path) == "<settings>", "Corrupt configuration never overwritten");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        private static void TestLayout()
        {
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (string text in new[] { "0%", "73%", "100%+", "Unavailable", "Offline", "Select mouse", "Stale" })
            using (var bitmap = new Bitmap(BarRenderer.Scale(800, dpi), BarRenderer.Scale(24, dpi)))
            using (var g = Graphics.FromImage(bitmap))
            {
                var settings = new Settings { Items = new List<string> { "MouseBattery", "Tracks" } };
                var sample = InfoTests.Demo(); sample.Briefing.MouseBattery = new Reading(text, "sample", true);
                int hidden; var cells = BarRenderer.Layout(g, new Rectangle(Point.Empty, bitmap.Size), settings, sample, dpi, out hidden);
                Require(hidden == 0 && cells.Count == 2 && cells[0].Bounds.Right <= cells[1].Bounds.Left && cells[0].Label == "MOUSE" && cells[0].Value == text, "Mouse values fit without overlapping work");
                Require(sample.FrozenCopy().Value("MouseBattery") == text, "Dragging work keeps mouse snapshot");
            }
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Mouse battery test: " + message); }

        private sealed class FakeSocket : WebSocket
        {
            internal string Fault;
            private readonly Queue<byte[]> frames = new Queue<byte[]>();
            private int offset;
            private bool broadcast = true;
            public override WebSocketCloseStatus? CloseStatus { get { return null; } }
            public override string CloseStatusDescription { get { return null; } }
            public override string SubProtocol { get { return "json"; } }
            public override WebSocketState State { get { return WebSocketState.Open; } }
            public override void Abort() { }
            public override void Dispose() { }
            public override Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken token) { return Task.FromResult(0); }
            public override Task CloseOutputAsync(WebSocketCloseStatus status, string description, CancellationToken token) { return Task.FromResult(0); }
            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
            {
                var request = JsonData.Parse(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count));
                Require(JsonData.Text(request, "verb") == "GET", "Transport sends GET");
                string response = new JavaScriptSerializer().Serialize(new { msgId = request["msgId"], path = request["path"], payload = JsonData.Parse(ListJson) });
                frames.Enqueue(Encoding.UTF8.GetBytes(Fault == "large" ? new string('x', LogitechBattery.MaxMessageBytes + 1) : response));
                return Task.FromResult(0);
            }
            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
            {
                if (Fault == "timeout") await Task.Delay(10000, token);
                if (Fault == "binary" || Fault == "closed") return new WebSocketReceiveResult(0, Fault == "binary" ? WebSocketMessageType.Binary : WebSocketMessageType.Close, true);
                if (broadcast || Fault == "flood")
                {
                    broadcast = false; byte[] notice = Encoding.UTF8.GetBytes("{\"msgId\":\"elsewhere\",\"path\":\"/battery/state/changed\"}");
                    Array.Copy(notice, 0, buffer.Array, buffer.Offset, notice.Length);
                    return new WebSocketReceiveResult(notice.Length, WebSocketMessageType.Text, true);
                }
                byte[] frame = frames.Peek(); int count = Math.Min(Math.Min(buffer.Count, 83), frame.Length - offset);
                Array.Copy(frame, offset, buffer.Array, buffer.Offset, count); offset += count;
                bool end = offset == frame.Length;
                if (end) { frames.Dequeue(); offset = 0; }
                return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
            }
        }
    }
}
