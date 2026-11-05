using System;
using Microsoft.AspNetCore.Builder;

namespace HostSample
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // No services.AddWebForms(): under webforcore-host, System.Web's hosting startup makes this a worker.
            var app = WebApplication.CreateBuilder(args).Build();
            app.UseWebForms(options =>
            {
                options.HandleAllRequestsWithWebForms();
                // HOST_SAMPLE_RECYCLE_REQUESTS: an application pool recycle after that many requests (#193).
                if (long.TryParse(Environment.GetEnvironmentVariable("HOST_SAMPLE_RECYCLE_REQUESTS"), out long requests))
                    options.Recycling(r => r.Requests = requests);
            });
            app.Run();
        }
    }
}
