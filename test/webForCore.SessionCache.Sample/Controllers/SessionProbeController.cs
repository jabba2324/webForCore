using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Web.SessionState;

namespace SessionCacheSample
{
    // Whether the request has session state (HttpContext.Session), and a value set in it if it has: the probes of #185,
    // which UseAspNetCoreSessionProvider() mustn't turn on where Web.config, the page or the controller turned it off.
    public static class SessionProbe
    {
        public static string Report(HttpContext context)
        {
            var text = new StringBuilder();
            HttpSessionState session = context.Session;
            text.Append("session=").Append(session == null ? "null" : "present").Append('\n');
            if (session != null)
            {
                session["probe"] = "1";
                text.Append("mode=").Append(session.Mode).Append('\n');
                text.Append("id=").Append(session.SessionID).Append('\n');
            }
            return text.ToString();
        }
    }

    // /mvc/SessionProbe: session state as the controller's default (SessionStateBehavior.Default: required).
    public class SessionProbeController : Controller
    {
        public ActionResult Index() => Content(SessionProbe.Report(System.Web.HttpContext.Current), "text/plain");
    }

    // /mvc/Sessionless: [SessionState(SessionStateBehavior.Disabled)], as on IIS no session state for its actions.
    [SessionState(SessionStateBehavior.Disabled)]
    public class SessionlessController : Controller
    {
        public ActionResult Index() => Content(SessionProbe.Report(System.Web.HttpContext.Current), "text/plain");
    }
}
