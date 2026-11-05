using Microsoft.AspNetCore.Builder;

namespace RequestsSample
{
    // The Kestrel entry point, under webforcore-host (RequestHandlingTests). IIS starts the site from Global.asax.
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
