using System;
using Microsoft.AspNetCore.Builder;

namespace MachineKeySample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = WebApplication.CreateBuilder(args).Build();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // The site of one <machineKey> configuration (its Web.config only), and the application's virtual path:
                // MachineKeyInteropTests runs this build once for each.
                string site = Environment.GetEnvironmentVariable("MACHINEKEY_SITE");
                if (!string.IsNullOrEmpty(site)) options.PhysicalPath(site);
                string virtualPath = Environment.GetEnvironmentVariable("MACHINEKEY_VIRTUAL_PATH");
                if (!string.IsNullOrEmpty(virtualPath)) options.VirtualPath(virtualPath);
            });
            app.Run();
        }
    }
}
