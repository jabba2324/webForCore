using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace SmokeMvc
{
    public class MvcApplication : HttpApplication
    {
        protected void Application_Start()
        {
            UnityMvcActivator.Start();
            // #195: System.Data.SqlClient loaded before Web API, MVC and SignalR scan the application's assemblies.
            System.GC.KeepAlive(typeof(System.Data.SqlClient.SqlConnection));
            System.Web.Http.GlobalConfiguration.Configure(config =>
                System.Web.Http.HttpRouteCollectionExtensions.MapHttpRoute(config.Routes, "Api", "api/{controller}"));
            RouteTable.Routes.MapRoute("Default", "{controller}/{action}/{id}",
                new { controller = "Home", action = "Index", id = UrlParameter.Optional });
        }
    }
}
