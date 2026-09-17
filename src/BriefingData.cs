using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal sealed class WorkItem
    {
        public string Keyword { get; set; }
        public string Notes { get; set; }
        public string Link { get; set; }
        public WorkItem() { Keyword = ""; Notes = ""; Link = ""; }
        internal WorkItem Copy() { return (WorkItem)MemberwiseClone(); }
        internal void Validate()
        {
            Keyword = Limit(Keyword, 150); Link = Limit(Link, 2000);
            Notes = Notes ?? ""; Notes = Notes.Substring(0, Math.Min(Notes.Length, 8000));
        }
        private static string Limit(string text, int length) { text = (text ?? "").Trim(); return text.Substring(0, Math.Min(text.Length, length)); }
        internal XElement Save() { return new XElement("work", new XAttribute("keyword", Keyword), new XElement("notes", Notes), new XElement("link", Link)); }
        internal static WorkItem Load(XElement value) { return new WorkItem { Keyword = (string)value.Attribute("keyword") ?? (string)value.Attribute("title") ?? "", Notes = (string)value.Element("notes") ?? "", Link = (string)value.Element("link") ?? "" }; }
        public override string ToString() { return string.IsNullOrWhiteSpace(Keyword) ? "未命名关键词" : Keyword; }
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
        internal Reading Codex = new Reading("Connecting…", "正在通过本机 Codex 的只读 app-server 接口读取额度。");
        internal Reading Location = new Reading("", "只显示本地 IP；公网位置尚未获取。");
        internal string WorkSummary = "Add keyword…";
        internal List<string> WorkKeywords = new List<string>();
        internal static string SummarizeWork(IEnumerable<WorkItem> work)
        {
            var items = work.Where(x => !string.IsNullOrWhiteSpace(x.Keyword)).ToList();
            if (items.Count == 0) return "Add keyword…";
            return string.Join("  ·  ", items.Take(3).Select(x => x.Keyword).ToArray()) + (items.Count > 3 ? "  +" + (items.Count - 3) : "");
        }
    }

}
