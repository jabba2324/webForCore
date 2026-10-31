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
            // Session state for TempData (SessionStateTempDataProvider), as in the TestApp.
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession();
            var app = builder.Build();
            app.UseSession();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider();
                // GATE_RECYCLE_CHECK: seconds between unload checks, so a test sees an old domain collected sooner.
                if (int.TryParse(System.Environment.GetEnvironmentVariable("GATE_RECYCLE_CHECK"), out int seconds))
                    options.Recycling(r => r.CheckInterval = System.TimeSpan.FromSeconds(seconds));
            });
            app.Run();
        }
    }
}
