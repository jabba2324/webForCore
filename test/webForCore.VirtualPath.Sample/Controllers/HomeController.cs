using System.Text;
using System.Web.Mvc;

namespace VirtualPathSample.Controllers
{
    // The URLs MVC generates in an application under a virtual path (#190), as text: Url.Content, Url.Action, Url.RouteUrl
    // and the request's paths.
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            var text = new StringBuilder();
            text.Append("controller=Home\n");
            text.Append("content=").Append(Url.Content("~/")).Append('\n');
            text.Append("content_script=").Append(Url.Content("~/Scripts/site.js")).Append('\n');
            text.Append("action=").Append(Url.Action("About")).Append('\n');
            text.Append("action_index=").Append(Url.Action("Index", "Home")).Append('\n');
            text.Append("route=").Append(Url.RouteUrl("Default", new { controller = "Home", action = "About", id = 7 })).Append('\n');
            text.Append("route_http=").Append(Url.RouteUrl("Default", new { controller = "Home", action = "About" }, "http")).Append('\n');
            text.Append("api=").Append(Url.HttpRouteUrl("DefaultApi", new { controller = "links", id = 3 })).Append('\n');
            text.Append("application_path=").Append(Request.ApplicationPath).Append('\n');
            text.Append("path=").Append(Request.Path).Append('\n');
            text.Append("raw_url=").Append(Request.RawUrl).Append('\n');
            text.Append("app_relative=").Append(Request.AppRelativeCurrentExecutionFilePath).Append('\n');
            return Content(text.ToString(), "text/plain");
        }

        public ActionResult About() => Content("about " + Request.Path, "text/plain");

        // Redirects an HTTP request to HTTPS: behind a proxy that ends TLS, only when the application sees the request as
        // secure (#194).
        [RequireHttps]
        public ActionResult Secure() => Content("secure " + Request.Url.Scheme, "text/plain");

        public ActionResult Back() => RedirectToAction("About");
    }
}
