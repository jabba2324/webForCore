using System.Linq;
using System.Web;
using Ninject;
using PackagesSample.App_Start;
using PackagesSample.Services;

namespace PackagesSample
{
    // Ninject.Web.Common.WebHost outside OWIN and Web API, on the kernel NinjectWebCommon made: InRequestScope keyed on
    // HttpContext.Current (the same object for the whole request, disposed by OnePerRequestHttpModule at EndRequest, before
    // the next request), and the modules NinjectWebCommon.Start registered with DynamicModuleUtility.
    public class ScopeHandler : IHttpHandler
    {
        private static string s_previous;

        public bool IsReusable => false;

        public void ProcessRequest(HttpContext context)
        {
            IKernel kernel = NinjectWebCommon.Kernel;
            var first = kernel.Get<IRequestService>();
            var second = kernel.Get<IRequestService>();
            var greeter = kernel.Get<IGreeter>();
            string previous = s_previous;
            s_previous = first.Id;
            context.Response.ContentType = "application/json";
            context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                started = Events.All.Contains("NinjectWebCommon.Start"),
                requestSame = ReferenceEquals(first, second) && ReferenceEquals(first, greeter.Request),
                newForThisRequest = first.Id != previous,
                previousDisposed = previous == null ? (bool?)null : Events.Disposed(previous),
                modules = context.ApplicationInstance.Modules.AllKeys.Select(k => context.ApplicationInstance.Modules[k].GetType().FullName)
                    .Where(n => n.StartsWith("Ninject")).Distinct().OrderBy(n => n).ToArray(),
            }));
        }
    }
}
