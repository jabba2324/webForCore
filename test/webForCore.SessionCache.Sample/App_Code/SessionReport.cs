using System;
using System.Linq;
using System.Text;
using System.Web.SessionState;

namespace SessionCacheSample
{
    public static class SessionReport
    {
        public static string Write(HttpSessionState session, long entered)
        {
            var text = new StringBuilder();
            text.Append("instance=").Append(Environment.GetEnvironmentVariable("SESSIONCACHE_INSTANCE")).Append('\n');
            text.Append("appid=").Append(System.Web.HttpRuntime.AppDomainAppId).Append('\n');
            text.Append("applmdpath=").Append(System.Web.HttpContext.Current.Request.ServerVariables["APPL_MD_PATH"]).Append('\n');
            text.Append("entered=").Append(entered).Append('\n');
            text.Append("left=").Append(DateTime.UtcNow.Ticks).Append('\n');
            text.Append("id=").Append(session.SessionID).Append('\n');
            text.Append("new=").Append(session.IsNewSession).Append('\n');
            text.Append("mode=").Append(session.Mode).Append('\n');
            text.Append("timeout=").Append(session.Timeout).Append('\n');
            text.Append("readonly=").Append(session.IsReadOnly).Append('\n');
            text.Append("count=").Append(session.Count).Append('\n');
            foreach (string key in session.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal))
            {
                object value = session[key];
                var basket = value as Basket;
                text.Append("value.").Append(key).Append('=')
                    .Append(basket != null ? "Basket(" + basket.Owner + "," + basket.Items + ")" : Convert.ToString(value)).Append('\n');
            }
            return text.ToString();
        }
    }
}
