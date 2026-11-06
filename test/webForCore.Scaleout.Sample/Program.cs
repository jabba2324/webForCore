using Microsoft.AspNetCore.Builder;
using WebFormsForCore.Hosting.IIS;

namespace ScaleoutSample
{
    // Run by ScaleoutTests under webforcore-host (#56), twice: two instances of one application, as two servers of a
    // web farm.
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
