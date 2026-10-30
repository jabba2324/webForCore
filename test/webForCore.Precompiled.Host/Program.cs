using Microsoft.AspNetCore.Builder;
using WebFormsForCore.Hosting.IIS;

namespace PrecompiledHost
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddWebForms();
            var app = builder.Build();
            app.UseWebForms(options => options.HandleAllRequestsWithWebForms());
            app.Run();
        }
    }
}
