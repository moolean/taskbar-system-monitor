using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal sealed class WorkItem
    {
        public string Title { get; set; }
        public string Status { get; set; }
        public int Progress { get; set; }
        public string Notes { get; set; }
        public string Link { get; set; }
        public WorkItem() { Title = ""; Status = "进行中"; Notes = ""; Link = ""; }
        internal WorkItem Copy() { return (WorkItem)MemberwiseClone(); }
        internal void Validate()
        {
            Title = Limit(Title, 150); Notes = Limit(Notes, 8000); Link = Limit(Link, 2000);
            Progress = Math.Max(0, Math.Min(100, Progress));
            if (Status != "待办" && Status != "阻塞" && Status != "完成") Status = "进行中";
        }
        private static string Limit(string text, int length) { text = (text ?? "").Trim(); return text.Substring(0, Math.Min(text.Length, length)); }
        internal XElement Save() { return new XElement("work", new XAttribute("title", Title), new XAttribute("status", Status), new XAttribute("progress", Progress), new XElement("notes", Notes), new XElement("link", Link)); }
        internal static WorkItem Load(XElement value) { return new WorkItem { Title = (string)value.Attribute("title") ?? "", Status = (string)value.Attribute("status") ?? "进行中", Progress = (int?)value.Attribute("progress") ?? 0, Notes = (string)value.Element("notes") ?? "", Link = (string)value.Element("link") ?? "" }; }
        public override string ToString() { return Title + " · " + Status + " " + Progress + "%"; }
    }

    internal static class LocalSecret
    {
        internal static string Seal(string value) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
        internal static string Open(string value) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
    }

    internal sealed class Reading
    {
        internal string Short, Details;
        internal DateTime UpdatedUtc;
        internal bool Available;
        internal Reading(string summary, string details, bool available = false)
        { Short = summary; Details = details; Available = available; UpdatedUtc = DateTime.UtcNow; }
        internal string FullDetails { get { return Details + "\r\n\r\n状态更新时间：" + UpdatedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss"); } }
    }

    internal sealed class BriefingData
    {
        internal Reading Codex = new Reading("连接中…", "正在通过本机 Codex 的只读 app-server 接口读取额度。");
        internal Reading Calendar = new Reading("待连接 · 点击配置", "在设置 → 数据连接填写飞书 CalDAV 专用账户，勿使用飞书登录密码。");
        internal Reading Location = new Reading("", "只显示本地 IP；公网位置尚未获取。");
        internal string WorkSummary = "添加工作追踪…";
        internal List<Meeting> Meetings = new List<Meeting>();
        internal static string SummarizeWork(IEnumerable<WorkItem> work)
        {
            var active = work.Where(x => x.Status != "完成").ToList();
            if (active.Count == 0) return work.Any() ? "全部完成 · 点击查看" : "添加工作追踪…";
            return string.Join("  ·  ", active.Take(3).Select(x => x.Title + " " + (x.Status == "阻塞" ? "阻塞" : x.Progress + "%")).ToArray()) + (active.Count > 3 ? "  +" + (active.Count - 3) : "");
        }
    }

    internal sealed class Meeting
    {
        internal string Id = "", Title = "", Notes = "", Location = "", Url = "";
        internal DateTimeOffset Start, End;
        internal bool AllDay;
        internal string Summary(DateTimeOffset now)
        {
            string when = AllDay ? "全天" : Start.LocalDateTime.ToString(Start.LocalDateTime.Date == now.LocalDateTime.Date ? "HH:mm" : "MM/dd HH:mm");
            string countdown = Start <= now ? "进行中" : (Start - now).TotalMinutes < 60 ? (int)Math.Ceiling((Start - now).TotalMinutes) + " 分钟后" : "";
            return when + " " + Title + (countdown.Length > 0 ? " · " + countdown : "");
        }
        internal string Details { get { return Title + "\r\n" + Start.LocalDateTime.ToString("yyyy-MM-dd HH:mm") + " — " + End.LocalDateTime.ToString("HH:mm") + (AllDay ? "（全天）" : "") + "\r\n" + Location + "\r\n\r\n" + Notes; } }
    }
}
