using System.Web.Http;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(WebApiSample.Startup))]

namespace WebApiSample
{
    // OWIN startup found by Microsoft.Owin.Host.SystemWeb: the same controllers under /owin through app.UseWebApi.
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.Map("/owin", owin =>
            {
                var config = new HttpConfiguration();
                WebApiConfig.Register(config);
                owin.UseWebApi(config);
            });
        }
    }
}
