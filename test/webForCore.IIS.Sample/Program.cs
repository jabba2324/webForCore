using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebFormsForCore.Hosting.IIS;

namespace IISSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // IIS's compression and W3C logging (App_Data is a hidden segment, so the logs are not served).
            builder.Services.AddWebForms(o => o.LogDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs"));
            var app = builder.Build();
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms());
            app.Run();
        }
    }
}
