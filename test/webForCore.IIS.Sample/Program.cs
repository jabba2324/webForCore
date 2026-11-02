using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebFormsForCore.Hosting.IIS;

namespace IISSample
{
    // Run by the gate suite under webforcore-host (#56), which owns the bindings: HTTP and HTTPS with client certificates
    // accepted (IISGate.SampleServer gives the host its "Kestrel" endpoints).
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            // IIS's compression and W3C logging. The logs are in a plain folder of the site on purpose: webForCore must not
            // serve them (RegressionTests, #15).
            builder.Services.AddWebForms(o => o.LogDirectory = Path.Combine(builder.Environment.ContentRootPath, "w3c-logs", "W3SVC1"));
            var app = builder.Build();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // GATE_RECYCLE_CHECK: seconds between checks of the recycling limits.
                if (int.TryParse(Environment.GetEnvironmentVariable("GATE_RECYCLE_CHECK"), out int seconds))
                    options.Recycling(r => r.CheckInterval = TimeSpan.FromSeconds(seconds));
            });
            app.Run();
        }
    }
}
