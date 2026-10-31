using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SmokeMvc
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            RouteTable.Routes.MapRoute("Default", "{controller}/{action}/{id}",
                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
