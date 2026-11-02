using Microsoft.AspNetCore.Builder;

namespace HostSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // No services.AddWebForms(): under webforcore-host, System.Web's hosting startup makes this a worker.
            var app = WebApplication.CreateBuilder(args).Build();
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms());
            app.Run();
        }
    }
}
