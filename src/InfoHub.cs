using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace TaskbarSystemMonitor
{
    internal sealed class InfoHub : IDisposable
    {
        private readonly object gate = new object();
        private BriefingData data = new BriefingData();
        private int generation;
        private bool stopped;
        private readonly HashSet<string> busy = new HashSet<string>();
        private readonly Dictionary<string, DateTime> next = new Dictionary<string, DateTime>();
        internal void Reset() { lock (gate) { generation++; next.Clear(); data = new BriefingData(); } }
        internal BriefingData Snapshot(Settings settings)
        {
            lock (gate)
            {
                Reading location = settings.GeoEnabled ? FreshLocation(data.Location, DateTime.UtcNow) : new Reading("", "公网查询已关闭，仅显示本地 IP。可在设置 → 数据连接了解并开启 IP 归属地查询。") { UpdatedUtc = data.Location.UpdatedUtc };
                return new BriefingData { Codex = data.Codex, Location = location, MouseBattery = LogitechBattery.Fresh(data.MouseBattery), WorkSummary = BriefingData.SummarizeWork(settings.WorkItems), WorkKeywords = settings.WorkItems.Select(x => x.Keyword).ToList() };
            }
        }
        internal void Pulse(Settings settings)
        {
            if (settings.Items.Contains("MouseBattery")) Schedule("mouse", LogitechBattery.RefreshSeconds, settings, delegate(Settings config)
            {
                Reading result = LogitechBattery.Read(config.MouseName).Reading;
                return delegate { data.MouseBattery = result; };
            });
            if (settings.Items.Contains("Codex")) Schedule("codex", 300, settings, delegate(Settings config)
            {
                Reading result;
                try { result = CodexQuota.Read(config.CodexPath); }
                catch (Exception error) { result = new Reading("Unavailable", SafeError(error)); }
                return delegate { data.Codex = result; };
            });
            if (settings.GeoEnabled) Schedule("geo", CityRefreshSeconds, settings, delegate(Settings config)
            {
                Reading result;
                try { result = ReadLocation(); }
                catch (Exception error) { result = new Reading("Unavailable", "暂时无法查询出口城市。\r\n" + SafeError(error)); }
                return delegate { data.Location = result; };
            });
        }
        private void Schedule(string key, int seconds, Settings config, Func<Settings, Action> fetch)
        {
            int revision;
            lock (gate)
            {
                DateTime due;
                if (stopped || busy.Contains(key) || (next.TryGetValue(key, out due) && due > DateTime.UtcNow)) return;
                busy.Add(key); next[key] = DateTime.UtcNow.AddSeconds(seconds); revision = generation;
                config = config.Copy(); // Copy only for a due worker, not on every 250 ms UI tick.
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Action publish = fetch(config); lock (gate) { if (!stopped && revision == generation) publish(); } }
                catch { /* Never leak credentials or endpoint responses into logs. */ }
                finally { lock (gate) busy.Remove(key); }
            });
        }
        internal const int CityRefreshSeconds = 60;
        internal static Reading FreshLocation(Reading reading, DateTime now)
        {
            // On resume don't claim a pre-sleep city is a current reading.
            return reading.Available && now - reading.UpdatedUtc > TimeSpan.FromSeconds(CityRefreshSeconds + 10)
                ? new Reading("Updating…", "旧城市已过期，正在等待新查询。") { UpdatedUtc = reading.UpdatedUtc } : reading;
        }
        internal static Reading ReadLocation()
        {
            return ParseLocation(SafeHttp.Read(new Uri("https://ipwho.is/?fields=success,city")));
        }
        internal static Reading ParseLocation(string json)
        {
            var response = JsonData.Parse(json);
            if (!object.Equals(JsonData.Get(response, "success"), true)) throw new InvalidOperationException("IP 查询服务暂不可用。");
            string city = (JsonData.Get(response, "city") as string ?? "").Trim();
            if (city.Length == 0 || city.Length > 100 || city.Any(char.IsControl)) throw new InvalidOperationException("服务未返回有效城市。");
            return new Reading(city, "网络出口城市：" + city + "\r\n来源：ipwho.is。按出口 IP 估算，非 GPS；代理或 VPN 会改变结果。每分钟刷新。", true);
        }
        internal static string SafeError(Exception error)
        {
            var web = error as WebException;
            if (web != null)
            {
                var response = web.Response as HttpWebResponse;
                if (response != null) { int code = (int)response.StatusCode; response.Dispose(); return "服务返回 HTTP " + code + "。请检查连接信息、授权或稍后重试。"; }
                return "网络连接失败或超时，请检查网络后重试。";
            }
            if (error is InvalidOperationException || error is TimeoutException) return error.Message;
            return "暂时无法读取，请检查配置。未获取的数据不会显示为零。";
        }
        public void Dispose() { lock (gate) { stopped = true; generation++; } }
    }
}
