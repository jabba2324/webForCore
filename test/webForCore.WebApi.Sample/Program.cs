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
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms());
            app.Run();
        }
    }
}
