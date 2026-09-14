using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TaskbarSystemMonitor
{
    internal static class SafeHttp
    {
        internal static string Read(Uri uri, string method, string body = null, string authorization = null, string depth = null)
        {
            if (uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new InvalidOperationException("只允许 HTTPS 地址，不能把密码放入 URL。");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = method; request.AllowAutoRedirect = false; request.Timeout = 10000; request.ReadWriteTimeout = 10000;
            request.UserAgent = "TaskbarSystemMonitor/3.0";
            if (authorization != null) request.Headers[HttpRequestHeader.Authorization] = authorization;
            if (depth != null) request.Headers["Depth"] = depth;
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body); request.ContentType = "application/xml; charset=utf-8"; request.ContentLength = bytes.Length;
                using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode >= 300) throw new InvalidOperationException("数据源要求跳转，请填写其最终 HTTPS 地址。");
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var text = new StringBuilder(); var buffer = new char[4096]; int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    { if (text.Length + count > 2097152) throw new InvalidOperationException("数据源响应过大。"); text.Append(buffer, 0, count); }
                    return text.ToString();
                }
            }
        }
    }

    internal static class CalendarSource
    {
        private static readonly XNamespace Dav = "DAV:", Cal = "urn:ietf:params:xml:ns:caldav";
        internal static List<Meeting> Read(Settings settings)
        {
            Uri root = ValidateUrl(settings.CalendarUrl);
            string auth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.CalendarUser + ":" + LocalSecret.Open(settings.CalendarSecret)));
            string properties = "<d:current-user-principal/><c:calendar-home-set/><d:resourcetype/>";
            XDocument first = Properties(root, auth, properties, "0");
            string home = Href(first, Cal + "calendar-home-set");
            if (home == null)
            {
                string principal = Href(first, Dav + "current-user-principal");
                if (principal != null) home = Href(Properties(SameOrigin(root, principal), auth, properties, "0"), Cal + "calendar-home-set");
            }
            var calendars = new List<Uri>();
            if (first.Descendants(Cal + "calendar").Any()) calendars.Add(root);
            else
            {
                Uri homeUri = home == null ? root : SameOrigin(root, home);
                XDocument listing = Properties(homeUri, auth, "<d:resourcetype/><d:displayname/>", "1");
                foreach (var response in listing.Descendants(Dav + "response"))
                    if (response.Descendants(Cal + "calendar").Any() && response.Element(Dav + "href") != null)
                        calendars.Add(SameOrigin(root, (string)response.Element(Dav + "href")));
            }
            if (calendars.Count == 0) throw new InvalidOperationException("未找到可读日历，请检查 CalDAV 服务器地址与同步权限。");
            if (calendars.Count > 20) throw new InvalidOperationException("日历数量超过 20，请填写具体的 CalDAV 日历地址。");
            var now = DateTimeOffset.UtcNow;
            string from = now.AddDays(-1).ToString("yyyyMMdd'T'HHmmss'Z'"), to = now.AddDays(14).ToString("yyyyMMdd'T'HHmmss'Z'");
            string query = "<c:calendar-query xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'><d:prop><c:calendar-data><c:expand start='" + from + "' end='" + to + "'/></c:calendar-data></d:prop><c:filter><c:comp-filter name='VCALENDAR'><c:comp-filter name='VEVENT'><c:time-range start='" + from + "' end='" + to + "'/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>";
            var meetings = new List<Meeting>();
            foreach (Uri calendar in calendars)
            {
                var response = Xml(SafeHttp.Read(calendar, "REPORT", query, auth, "1"));
                foreach (var data in response.Descendants(Cal + "calendar-data")) meetings.AddRange(ParseIcal(data.Value));
            }
            return meetings.Where(x => x.End > now && x.Start < now.AddDays(14)).GroupBy(x => x.Id + "|" + x.Start.ToString("o")).Select(x => x.First()).OrderBy(x => x.Start).Take(100).ToList();
        }
        internal static Uri ValidateUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidOperationException("CalDAV 需要不含密码、查询参数的 HTTPS 服务器地址。");
            return uri;
        }
        internal static Uri SameOrigin(Uri root, string path)
        {
            Uri uri = new Uri(root, path);
            if (uri.Scheme != "https" || uri.Authority != root.Authority || uri.UserInfo.Length > 0)
                throw new InvalidOperationException("CalDAV 返回了其他服务器的地址，已阻止发送凭据。");
            return uri;
        }
        private static XDocument Properties(Uri uri, string auth, string properties, string depth)
        { return Xml(SafeHttp.Read(uri, "PROPFIND", "<d:propfind xmlns:d='DAV:' xmlns:c='urn:ietf:params:xml:ns:caldav'><d:prop>" + properties + "</d:prop></d:propfind>", auth, depth)); }
        private static string Href(XDocument doc, XName property) { var node = doc.Descendants(property).Elements(Dav + "href").FirstOrDefault(); return node == null ? null : node.Value; }
        private static XDocument Xml(string text)
        {
            using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 })) return XDocument.Load(reader);
        }
        internal static List<Meeting> ParseIcal(string text)
        {
            var unfolded = new List<string>();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            { if ((line.StartsWith(" ") || line.StartsWith("\t")) && unfolded.Count > 0) unfolded[unfolded.Count - 1] += line.Substring(1); else unfolded.Add(line.TrimEnd('\r')); }
            var events = new List<Meeting>(); Meeting item = null;
            bool cancelled = false, recurring = false; int nested = 0; string duration = null;
            foreach (string line in unfolded)
            {
                if (line == "BEGIN:VEVENT") { item = new Meeting(); cancelled = recurring = false; nested = 0; duration = null; continue; }
                if (line == "END:VEVENT" && item != null)
                {
                    if (!cancelled)
                    {
                        if (recurring) throw new InvalidOperationException("日历服务器没有展开重复日程，暂不能可靠判断下一场会议。");
                        if (item.Start == default(DateTimeOffset)) throw new InvalidOperationException("日历包含缺少开始时间的日程。");
                        if (item.End == default(DateTimeOffset)) item.End = duration != null ? item.Start.Add(XmlConvert.ToTimeSpan(duration)) : item.AllDay ? item.Start.AddDays(1) : item.Start;
                        if (item.Title.Length == 0) item.Title = "无标题日程";
                        events.Add(item);
                    }
                    item = null; continue;
                }
                if (item == null) continue;
                if (line.StartsWith("BEGIN:")) { nested++; continue; }
                if (line.StartsWith("END:")) { nested--; continue; }
                if (nested > 0) continue;
                int colon = line.IndexOf(':'); if (colon < 0) continue;
                string key = line.Substring(0, colon), value = line.Substring(colon + 1), name = key.Split(';')[0].ToUpperInvariant();
                switch (name)
                {
                    case "UID": item.Id = Unescape(value); break;
                    case "SUMMARY": item.Title = Unescape(value); break;
                    case "DESCRIPTION": item.Notes = Unescape(value); break;
                    case "LOCATION": item.Location = Unescape(value); break;
                    case "URL": item.Url = Unescape(value); break;
                    case "STATUS": cancelled = value == "CANCELLED"; break;
                    case "RRULE": recurring = true; break;
                    case "DTSTART": item.AllDay = value.Length == 8; item.Start = Date(value, key); break;
                    case "DTEND": item.End = Date(value, key); break;
                    case "DURATION": duration = value; break;
                }
            }
            return events;
        }
        private static DateTimeOffset Date(string value, string key)
        {
            DateTime date;
            if (!DateTime.TryParseExact(value.TrimEnd('Z'), new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new InvalidOperationException("无法解析日历时间。");
            date = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            if (value.EndsWith("Z")) return new DateTimeOffset(date, TimeSpan.Zero);
            TimeZoneInfo zone = TimeZoneInfo.Local;
            string tz = key.Split(';').FirstOrDefault(x => x.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase));
            if (tz != null)
            {
                string name = tz.Substring(5).Trim('"');
                var names = new Dictionary<string, string> { { "Asia/Shanghai", "China Standard Time" }, { "Asia/Hong_Kong", "China Standard Time" }, { "Asia/Tokyo", "Tokyo Standard Time" }, { "America/Los_Angeles", "Pacific Standard Time" }, { "America/New_York", "Eastern Standard Time" }, { "Europe/London", "GMT Standard Time" }, { "Etc/UTC", "UTC" } };
                if (names.ContainsKey(name)) name = names[name];
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(name); } catch { throw new InvalidOperationException("日历包含尚不支持的时区，请使用 UTC 或系统时区。"); }
            }
            if (zone.IsInvalidTime(date) || zone.IsAmbiguousTime(date)) throw new InvalidOperationException("日历时间落在无效或歧义的夏令时区间，请使用 UTC 时间。");
            return new DateTimeOffset(date, zone.GetUtcOffset(date));
        }
        private static string Unescape(string value)
        {
            var result = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            { if (value[i] == '\\' && i + 1 < value.Length) { i++; result.Append(value[i] == 'n' || value[i] == 'N' ? '\n' : value[i]); } else result.Append(value[i]); }
            return result.ToString();
        }
    }
}
