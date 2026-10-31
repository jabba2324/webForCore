using Microsoft.AspNetCore.Builder;
using System.Web;

namespace WebApiSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var app = WebApplication.CreateBuilder(args).Build();
            // The fork's default (classic) pipeline: its integrated pipeline needs IIS notifications and is not emulated.
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // GATE_RECYCLE_CHECK: seconds between unload checks, so a test sees an old domain collected sooner.
                if (int.TryParse(System.Environment.GetEnvironmentVariable("GATE_RECYCLE_CHECK"), out int seconds))
                    options.Recycling(r => r.CheckInterval = System.TimeSpan.FromSeconds(seconds));
            });
            app.Run();
        }
    }
}
