using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Web;

namespace MvcSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // Session state for TempData (SessionStateTempDataProvider) is in process, as <sessionState> (no mode: InProc)
            // had it on IIS: no UseAspNetCoreSessionProvider() (#180). AddSession() and UseSession() are here only to show
            // that ASP.NET Core's session sets no cookie of its own (GateTests.OneSessionCookie, #79).
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession();
            var app = builder.Build();
            app.UseSession();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // GATE_RECYCLE_CHECK: seconds between checks of the recycling limits.
                if (int.TryParse(System.Environment.GetEnvironmentVariable("GATE_RECYCLE_CHECK"), out int seconds))
                    options.Recycling(r => r.CheckInterval = System.TimeSpan.FromSeconds(seconds));
            });
            app.Run();
        }
    }
}
