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
                Reading location = settings.GeoEnabled ? data.Location : new Reading("", "公网查询已关闭，仅显示本地 IP。可在设置 → 数据连接了解并开启 IP 归属地查询。") { UpdatedUtc = data.Location.UpdatedUtc };
                return new BriefingData { Codex = data.Codex, Location = location, WorkSummary = BriefingData.SummarizeWork(settings.WorkItems), WorkKeywords = settings.WorkItems.Select(x => x.Keyword).ToList() };
            }
        }
        internal void Pulse(Settings settings)
        {
            if (settings.Items.Contains("Codex")) Schedule("codex", 300, settings.Copy(), delegate(Settings config)
            {
                Reading result;
                try { result = CodexQuota.Read(config.CodexPath); }
                catch (Exception error) { result = new Reading("Unavailable", SafeError(error)); }
                return delegate { data.Codex = result; };
            });
            if (settings.Items.Contains("Ip") && settings.GeoEnabled) Schedule("geo", 600, settings.Copy(), delegate(Settings config)
            {
                Reading result;
                try { result = ReadLocation(); }
                catch (Exception error) { result = new Reading("", "公网位置暂不可用；当前只显示本地 IP。\r\n" + SafeError(error)); }
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
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Action publish = fetch(config); lock (gate) { if (!stopped && revision == generation) publish(); } }
                catch { /* Never leak credentials or endpoint responses into logs. */ }
                finally { lock (gate) busy.Remove(key); }
            });
        }
        internal static Reading ReadLocation()
        {
            var response = JsonData.Parse(SafeHttp.Read(new Uri("https://ipwho.is/?fields=success,message,ip,city,region,country_code,connection.isp")));
            if (!object.Equals(JsonData.Get(response, "success"), true)) throw new InvalidOperationException("IP 查询服务暂不可用。");
            string ip = JsonData.Text(response, "ip"), city = JsonData.Text(response, "city"), region = JsonData.Text(response, "region");
            string place = city.Length > 0 ? city : region;
            return new Reading(ip + " · " + place, "公网出口 IP：" + ip + "\r\n大致位置：" + place + " / " + JsonData.Text(response, "country_code") + "\r\n网络：" + JsonData.Text(JsonData.Object(response, "connection"), "isp") + "\r\n\r\n来源：ipwho.is。位置根据公网出口 IP 推断，不是 GPS；代理、VPN 会改变结果。每 10 分钟刷新，可在设置中关闭。", true);
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
