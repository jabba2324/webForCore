using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web;

namespace WebPagesSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // Out-of-process session state in ASP.NET Core's distributed cache, for a site whose session state was out of
            // process on IIS (Web.config: <sessionState mode="StateServer">, which UseAspNetCoreSessionProvider() replaces).
            // Every value has to be serializable, as with StateServer (WebPagesTests, #180).
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddDistributedMemoryCache();
            var app = builder.Build();
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider());
            app.Run();
        }
    }
}
