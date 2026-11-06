using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web;

namespace SessionCacheSample
{
    // Nothing about session state or the output cache here: web.config configures both, as on IIS. With
    // SESSIONCACHE_ASPNETCORE_SESSION=1, session state is in ASP.NET Core's distributed cache (UseAspNetCoreSessionProvider()),
    // in place of the store web.config names, for AspNetCoreSessionTests (#185).
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            bool aspNetCoreSession = System.Environment.GetEnvironmentVariable("SESSIONCACHE_ASPNETCORE_SESSION") == "1";
            if (aspNetCoreSession) builder.Services.AddDistributedMemoryCache();
            var app = builder.Build();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                if (aspNetCoreSession) options.UseAspNetCoreSessionProvider();
            });
            app.Run();
        }
    }
}
