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
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider());
            app.Run();
        }
    }
}
