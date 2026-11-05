using System;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin;
using Microsoft.Owin.Security.Cookies;
using MachineKeyInterop;
using Owin;

namespace MachineKeySample
{
    // Katana's cookie middleware on System.Web, as an ASP.NET Identity application has it: a plain cookie ("Cookies") and
    // Identity's application cookie, with SecurityStampValidator. Microsoft.Owin.Host.SystemWeb gives the middleware the
    // machine key's data protection (MachineKey.Protect), so the cookies are protected with the <machineKey>.
    //   /owin/signin?type=T  signs in with the identity and properties of Interop.OwinIdentity and OwinProperties.
    //   /owin/check?type=T   authenticates the request's cookie of that type and answers "ok" or what differs.
    // A failure (the middleware protects the cookie after the request) answers 500 with "error: <exception type>", as
    // Interop.Produce records one.
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.Use(async (context, next) =>
            {
                Exception error = null;
                try
                {
                    await next();
                }
                catch (Exception e)
                {
                    error = e;
                }
                if (error != null)
                {
                    context.Response.StatusCode = 500;
                    context.Response.Headers.Remove("Set-Cookie");
                    await context.Response.WriteAsync(Interop.Error(error));
                }
            });
            app.CreatePerOwinContext(() => new UserManager<InteropUser>(new InteropUserStore()));
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = Interop.ApplicationCookieType,
                Provider = new CookieAuthenticationProvider
                {
                    OnValidateIdentity = SecurityStampValidator.OnValidateIdentity<UserManager<InteropUser>, InteropUser>(
                        TimeSpan.FromMinutes(30), (manager, user) => manager.CreateIdentityAsync(user, Interop.ApplicationCookieType)),
                },
            });
            app.UseCookieAuthentication(new CookieAuthenticationOptions { AuthenticationType = Interop.CookiesType });
            app.Use(async (context, next) =>
            {
                string type = context.Request.Query["type"];
                switch (context.Request.Path.Value)
                {
                    case "/owin/signin":
                        context.Authentication.SignIn(Interop.OwinProperties(), Interop.OwinIdentity(type));
                        return;
                    case "/owin/check":
                        var result = await context.Authentication.AuthenticateAsync(type);
                        await context.Response.WriteAsync(Interop.CheckOwin(type, result?.Identity, result?.Properties) ?? "ok");
                        return;
                }
                await next();
            });
        }
    }
}
