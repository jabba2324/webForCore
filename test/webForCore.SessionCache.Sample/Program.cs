using Microsoft.AspNetCore.Builder;
using System.Web;

namespace SessionCacheSample
{
    // Nothing about session state or the output cache here: web.config configures both, as on IIS.
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = WebApplication.CreateBuilder(args).Build();
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms());
            app.Run();
        }
    }
}
