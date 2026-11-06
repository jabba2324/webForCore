using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SessionCacheSample
{
    // MVC's route (/mvc/<controller>) for the session probes' controllers (Controllers/SessionProbeController.cs, #185).
    public class Global : HttpApplication
    {
        protected void Application_Start()
        {
            RouteTable.Routes.MapRoute("Default", "mvc/{controller}/{action}", new { action = "Index" });
        }
    }
}
