using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace TaskbarSystemMonitor
{
    internal static class JsonData
    {
        internal static Dictionary<string, object> Parse(string value) { return new JavaScriptSerializer { MaxJsonLength = 2097152 }.Deserialize<Dictionary<string, object>>(value); }
        internal static Dictionary<string, object> Object(Dictionary<string, object> parent, string key) { object value; return parent != null && parent.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        internal static object Get(Dictionary<string, object> parent, string key) { object value; return parent != null && parent.TryGetValue(key, out value) ? value : null; }
        internal static string Text(Dictionary<string, object> parent, string key) { return Convert.ToString(Get(parent, key)); }
    }

    internal static class CodexQuota
    {
        internal static string FindExecutable(string selected)
        {
            if (!string.IsNullOrWhiteSpace(selected)) return Path.GetFullPath(selected);
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                try { string path = Path.Combine(folder.Trim('"'), "codex.exe"); if (File.Exists(path)) return path; } catch { }
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                var match = Directory.GetDirectories(root).Select(x => Path.Combine(x, "codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (match != null) return match;
            }
            throw new InvalidOperationException("未找到 codex.exe，请在设置中指定本机 Codex 程序路径。");
        }
        internal static Reading Read(string selected)
        {
            string executable = FindExecutable(selected);
            if (!File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Codex 路径应指向本机 .exe 文件。");
            // A short-lived private stdio server reuses the CLI's normal sign-in.
            // No auth files are opened here, no threads are created, no resets used.
            return Parse(Request(executable));
        }
        private static Dictionary<string, object> Request(string executable)
        {
            using (var done = new ManualResetEvent(false))
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(executable, "app-server --stdio") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                Dictionary<string, object> result = null;
                string failure = null;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null) { done.Set(); return; }
                    try
                    {
                        var message = JsonData.Parse(e.Data);
                        string id = JsonData.Text(message, "id");
                        if (id != "1" && id != "2") return;
                        if (JsonData.Object(message, "error") != null) { failure = "无法读取 Codex 额度，请确认 CLI 已登录同一 ChatGPT 账户。"; done.Set(); return; }
                        if (id == "1")
                        {
                            process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
                            process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
                            process.StandardInput.Flush();
                        }
                        else { result = JsonData.Object(message, "result"); done.Set(); }
                    }
                    catch { failure = "Codex 返回了无法解析的数据。"; done.Set(); }
                };
                process.ErrorDataReceived += delegate { /* Drain without logging account data. */ };
                try
                {
                    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"taskbar_monitor\",\"title\":\"Taskbar Monitor\",\"version\":\"3.0.0\"}}}");
                    process.StandardInput.Flush();
                    if (!done.WaitOne(18000)) throw new TimeoutException("Codex 额度读取超时，稍后重试。");
                }
                finally
                {
                    try { process.StandardInput.Close(); if (!process.WaitForExit(1500)) { process.Kill(); process.WaitForExit(1500); } } catch { }
                    try { process.CancelOutputRead(); process.CancelErrorRead(); } catch { }
                }
                if (result == null) throw new InvalidOperationException(failure ?? "Codex 服务未返回额度；请检查登录与网络连接。");
                return result;
            }
        }
        internal static Reading Parse(Dictionary<string, object> data)
        {
            var buckets = JsonData.Object(data, "rateLimitsByLimitId");
            var main = JsonData.Object(buckets, "codex") ?? JsonData.Object(data, "rateLimits");
            var summary = Windows(main, false);
            var details = new List<string> { "以下为剩余额度；已用比例由服务端提供。" };
            if (buckets != null)
                foreach (var bucket in buckets)
                {
                    var value = bucket.Value as Dictionary<string, object>; if (value == null) continue;
                    string name = JsonData.Text(value, "limitName");
                    details.Add("\r\n" + (name.Length > 0 ? name : bucket.Key) + "\r\n" + string.Join("\r\n", Windows(value, true).ToArray()));
                }
            else details.AddRange(Windows(main, true));
            return new Reading(summary.Count == 0 ? "额度暂不可用" : string.Join(" · ", summary.ToArray()), string.Join("\r\n", details.ToArray()), summary.Count > 0);
        }
        private static List<string> Windows(Dictionary<string, object> bucket, bool detail)
        {
            var values = new List<string>();
            foreach (string key in new[] { "primary", "secondary" })
            {
                var window = JsonData.Object(bucket, key);
                if (window == null || JsonData.Get(window, "usedPercent") == null) continue;
                double remaining = Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(JsonData.Get(window, "usedPercent"))));
                int minutes = Convert.ToInt32(JsonData.Get(window, "windowDurationMins") ?? 0);
                string name = minutes == 10080 ? "周" : minutes == 300 ? "5h" : minutes > 0 ? minutes + "m" : "当前";
                string line = name + "剩余 " + remaining.ToString("0") + "%";
                if (detail && JsonData.Get(window, "resetsAt") != null)
                    line += " · 重置 " + new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Convert.ToDouble(JsonData.Get(window, "resetsAt"))).LocalDateTime.ToString("MM-dd HH:mm");
                values.Add(line);
            }
            return values;
        }
    }
}
