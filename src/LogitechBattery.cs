using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace TaskbarSystemMonitor
{
    internal sealed class LogitechMouse
    {
        internal string Id, Name, State;
    }
    internal sealed class MouseBatteryProbe
    {
        internal List<LogitechMouse> Devices = new List<LogitechMouse>();
        internal Reading Reading = new Reading("Unavailable", "尚未读取鼠标电量。");
    }

    // G HUB's local, unofficial protocol. Only two GET endpoints are allowed;
    // no HID writes, pairing, DPI, lighting, account or profile operations.
    internal static class LogitechBattery
    {
        internal const string Endpoint = "ws://127.0.0.1:9010";
        internal const int RefreshSeconds = 30, TimeoutMs = 5000, MaxMessageBytes = 262144;
        internal static MouseBatteryProbe Read(string preferredName = "")
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeoutMs))
                using (var socket = new ClientWebSocket())
                {
                    socket.Options.Proxy = null; // Local device data never goes through a proxy.
                    socket.Options.AddSubProtocol("json");
                    socket.Options.SetRequestHeader("Origin", "file://");
                    try
                    {
                        socket.ConnectAsync(new Uri(Endpoint), timeout.Token).GetAwaiter().GetResult();
                        return ReadFrom(delegate(string path) { return Query(socket, path, timeout.Token); }, preferredName);
                    }
                    finally { socket.Abort(); }
                }
            }
            catch (Exception error)
            {
                string detail = error is OperationCanceledException ? "读取超时，将自动重试。" :
                    error is WebSocketException ? "无法连接本机 G HUB；请让 G HUB 后台保持运行。" :
                    "G HUB 返回的数据暂不可用，可能是接口版本变化或设备尚未就绪。";
                return new MouseBatteryProbe { Reading = new Reading("Unavailable", detail + "\r\n只读本机接口，不依赖 Codex，不更改鼠标设置。") };
            }
        }

        internal static MouseBatteryProbe ReadFrom(Func<string, Dictionary<string, object>> query, string preferredName)
        {
            var result = new MouseBatteryProbe { Devices = Devices(query("/devices/list")) };
            var candidates = result.Devices.Where(x => string.IsNullOrEmpty(preferredName) || x.Name == preferredName).ToList();
            var active = candidates.Where(x => x.State == "ACTIVE").ToList();
            if (active.Count == 0)
            {
                result.Reading = new Reading("Offline", candidates.Count == 0 ? "G HUB 中未找到所选的电池供电鼠标。请检查接收器和设备选择。" : "鼠标处于休眠或离线状态；移动鼠标唤醒后会自动刷新。");
                return result;
            }
            if (active.Count != 1)
            {
                result.Reading = new Reading("Select mouse", "检测到多只活动鼠标，请在设置 → 数据连接中选择名称不同的目标设备。\r\n" + string.Join("、", active.Select(x => x.Name)));
                return result; // Never silently show another device's battery.
            }
            var mouse = active[0];
            result.Reading = ParseBattery(mouse, query(BatteryPath(mouse.Id)));
            return result;
        }

        internal static List<LogitechMouse> Devices(Dictionary<string, object> payload)
        {
            var result = new List<LogitechMouse>();
            var items = JsonData.Get(payload, "deviceInfos") as IEnumerable;
            if (items == null) return result;
            foreach (var value in items)
            {
                var item = value as Dictionary<string, object>;
                if (item == null || JsonData.Text(item, "deviceType") != "MOUSE" ||
                    !object.Equals(JsonData.Get(JsonData.Object(item, "capabilities"), "hasBatteryStatus"), true)) continue;
                string id = JsonData.Text(item, "id");
                if (!ValidId(id)) continue;
                string name = JsonData.Text(item, "displayName");
                if (string.IsNullOrWhiteSpace(name)) name = JsonData.Text(item, "extendedDisplayName");
                if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)) continue;
                result.Add(new LogitechMouse { Id = id, Name = name, State = JsonData.Text(item, "state") });
            }
            return result;
        }

        internal static Reading ParseBattery(LogitechMouse mouse, Dictionary<string, object> payload)
        {
            string prefix = mouse.Name + "\r\n";
            if (JsonData.Text(payload, "deviceId") != mouse.Id)
                return new Reading("Unavailable", prefix + "电量响应与所选鼠标不匹配。");
            if (object.Equals(JsonData.Get(payload, "isBatteryRemoved"), true))
                return new Reading("No battery", prefix + "G HUB 报告电池已移除。");
            if (object.Equals(JsonData.Get(payload, "chargingError"), true))
                return new Reading("Charge error", prefix + "G HUB 报告充电异常，请检查充电连接。");
            object value = JsonData.Get(payload, "percentage");
            if (!(value is int || value is long || value is double || value is decimal))
                return new Reading("Unavailable", prefix + "G HUB 未返回百分比，不把未知电量显示为 0%。");
            double percent = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(percent) || double.IsInfinity(percent) || percent < 0 || percent > 100 ||
                object.Equals(JsonData.Get(payload, "simpleBattery"), true))
                return new Reading("Unavailable", prefix + "该电量值无效或设备只支持粗略等级。");
            bool charging = object.Equals(JsonData.Get(payload, "charging"), true);
            string text = percent.ToString("0", CultureInfo.InvariantCulture) + "%";
            return new Reading(text + (charging ? "+" : ""),
                prefix + "电量：" + text + "\r\n状态：" + (charging ? "正在充电（横条的 +）" : percent <= 20 ? "低电量" : "电池供电") +
                "\r\n来源：本机 Logitech G HUB，每 30 秒刷新。\r\n电量精度与 G HUB 一致；后台不可用时显示 Unavailable，不保留旧数值冒充实时电量。", true);
        }

        internal static Reading Fresh(Reading value)
        {
            return value.Available && DateTime.UtcNow - value.UpdatedUtc > TimeSpan.FromSeconds(90)
                ? new Reading("Stale", "鼠标电量超过 90 秒未更新，正在等待新的 G HUB 数据。") : value;
        }
        private static bool ValidId(string id) { return Regex.IsMatch(id ?? "", @"\A[a-zA-Z0-9_-]{1,80}\z"); }
        internal static string BatteryPath(string id)
        {
            if (!ValidId(id)) throw new ArgumentException("Invalid local device identifier.");
            return "/battery/" + id + "/state";
        }
        internal static string Request(string path, string messageId)
        {
            if (path != "/devices/list" && !Regex.IsMatch(path ?? "", @"\A/battery/[a-zA-Z0-9_-]{1,80}/state\z"))
                throw new ArgumentException("Only read-only mouse endpoints are allowed.");
            return new JavaScriptSerializer().Serialize(new { msgId = messageId, verb = "GET", path = path });
        }
        internal static Dictionary<string, object> Payload(Dictionary<string, object> response, string id, string path)
        {
            if (JsonData.Text(response, "msgId") != id || JsonData.Text(response, "path") != path) return null;
            if (JsonData.Get(response, "error") != null) throw new InvalidDataException("G HUB returned an error.");
            var payload = JsonData.Object(response, "payload");
            if (payload == null) throw new InvalidDataException("Missing G HUB payload.");
            return payload;
        }
        internal static Dictionary<string, object> Query(WebSocket socket, string path, CancellationToken token)
        {
            string id = Guid.NewGuid().ToString("N");
            var request = Encoding.UTF8.GetBytes(Request(path, id));
            socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
            for (int i = 0; i < 32; i++)
            {
                var payload = Payload(JsonData.Parse(Receive(socket, token)), id, path);
                if (payload != null) return payload;
            }
            throw new InvalidDataException("No matching G HUB response.");
        }
        private static string Receive(WebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult received;
                do
                {
                    received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).GetAwaiter().GetResult();
                    if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > MaxMessageBytes)
                        throw new InvalidDataException("Unexpected G HUB message.");
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                return new UTF8Encoding(false, true).GetString(message.ToArray());
            }
        }
    }
}
