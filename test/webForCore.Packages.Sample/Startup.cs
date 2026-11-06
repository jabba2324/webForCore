using System.Web.Http;
using Microsoft.Owin;
using Ninject;
using Ninject.Web.Common.OwinHost;
using Ninject.Web.WebApi.OwinHost;
using Owin;
using PackagesSample.App_Start;
using PackagesSample.Services;

[assembly: OwinStartup(typeof(PackagesSample.Startup))]

namespace PackagesSample
{
    // Ninject.Web.Common.OwinHost and Ninject.Web.WebApi.OwinHost as their documentation has them: a kernel of the OWIN
    // pipeline's own (with the application's bindings), Web API through UseNinjectWebApi, and an OWIN middleware that
    // resolves from the kernel in the request scope UseNinjectMiddleware puts in the OWIN environment.
    public class Startup
    {
        private static IKernel s_kernel;
        private static string s_previous;

        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            WebApiConfig.Register(config);

            app.UseNinjectMiddleware(CreateKernel);
            app.Map("/owin/scope", owin => owin.Run(context =>
            {
                var first = s_kernel.Get<IOwinScoped>();
                var second = s_kernel.Get<IOwinScoped>();
                string previous = s_previous;
                s_previous = first.Id;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    owinScopedSame = ReferenceEquals(first, second),
                    newForThisRequest = first.Id != previous,
                    previousDisposed = previous == null ? (bool?)null : Events.Disposed(previous),
                    greeting = s_kernel.Get<IGreeter>().Greet("OWIN"),
                }));
            }));
            app.UseNinjectWebApi(config);
        }

        private static IKernel CreateKernel()
        {
            var kernel = new StandardKernel();
            NinjectWebCommon.RegisterServices(kernel);
            s_kernel = kernel;
            return kernel;
        }
    }
}
