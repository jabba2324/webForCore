using System.Web.Http;
using Swashbuckle.Application;

namespace PackagesSample
{
    // The Visual Studio "Web API" template's routes, with Swashbuckle.Core's documentation and UI.
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            config
                .EnableSwagger(c => c.SingleApiVersion("v1", "Packages sample"))
                .EnableSwaggerUi();
        }
    }
}
