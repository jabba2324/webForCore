using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace VirtualPathSample
{
    // The Kestrel entry point, under webforcore-host (VirtualPathTests, ForwardedHeadersTests). IIS starts the application
    // from Global.asax, as the application /app of a site.
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = WebApplication.CreateBuilder(args).Build();
            // GATE_FORWARDED=app: the application applies X-Forwarded-For and X-Forwarded-Proto itself, from a proxy on
            // this machine (ASP.NET Core's default KnownProxies, the loopback addresses), as an application behind a
            // load balancer does (#194).
            if (Environment.GetEnvironmentVariable("GATE_FORWARDED") == "app")
                app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // The application /app of a site, as on IIS (#190).
                options.VirtualPath("/app");
            });
            app.Run();
        }
    }
}
