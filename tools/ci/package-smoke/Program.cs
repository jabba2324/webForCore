using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebFormsForCore.Hosting.IIS;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddWebForms();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();

        var app = builder.Build();
        app.UseSession();
        app.UseWebForms(o => o
            .HandleAllRequestsWithWebForms()
            .UseAspNetCoreSessionProvider());
        app.Run();
    }
}
