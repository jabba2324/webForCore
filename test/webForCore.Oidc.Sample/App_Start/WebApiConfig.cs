using System.Web.Http;

namespace OidcSample
{
    // The Web API behind the bearer middleware (Startup.Auth.cs): /api/me and /idsrv-api/me.
    public static class WebApiConfig
    {
        public static HttpConfiguration Create()
        {
            var config = new HttpConfiguration();
            config.Routes.MapHttpRoute(name: "Api", routeTemplate: "{controller}");
            return config;
        }
    }
}
