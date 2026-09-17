using System;
using System.IO;
using System.Net;
using System.Text;

namespace TaskbarSystemMonitor
{
    internal static class SafeHttp
    {
        internal static string Read(Uri uri) { return Read(uri, 0); }
        private static string Read(Uri uri, int redirects)
        {
            if (!uri.IsAbsoluteUri || uri.Scheme != "https" || uri.UserInfo.Length > 0)
                throw new InvalidOperationException("只允许不含账号密码的 HTTPS 地址。");
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET"; request.AllowAutoRedirect = false;
            request.Timeout = 10000; request.ReadWriteTimeout = 10000;
            request.UserAgent = "TaskbarSystemMonitor/3.2";
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                int code = (int)response.StatusCode;
                if (code == 301 || code == 302 || code == 307 || code == 308)
                {
                    if (redirects >= 3 || string.IsNullOrWhiteSpace(response.Headers["Location"]))
                        throw new InvalidOperationException("数据源跳转次数过多或缺少目标地址。");
                    Uri destination = RedirectTarget(uri, response.Headers["Location"]);
                    response.Close();
                    return Read(destination, redirects + 1);
                }
                if (code >= 300) throw new InvalidOperationException("数据源返回了不支持的跳转。");
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    var text = new StringBuilder(); var buffer = new char[4096]; int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (text.Length + count > 2097152) throw new InvalidOperationException("数据源响应过大。");
                        text.Append(buffer, 0, count);
                    }
                    return text.ToString();
                }
            }
        }
        internal static Uri RedirectTarget(Uri origin, string location)
        {
            var target = new Uri(origin, location);
            if (target.Scheme != "https" || target.UserInfo.Length > 0 || target.Host != origin.Host || target.Port != origin.Port)
                throw new InvalidOperationException("不允许数据源跳转到其他服务器或非 HTTPS 地址。");
            return target;
        }
    }
}
