using System.Threading.Tasks;
using Microsoft.Owin;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Google;
using Owin;

[assembly: OwinStartup(typeof(SmokeMvc.Startup))]

namespace SmokeMvc
{
    // As the Visual Studio template's Startup.Auth.cs with Google sign-in (OpenOrderFramework, SocialGoal), on the
    // original Katana 3.0.1 package (#18). /challenge asks the Google middleware to sign in, which redirects to Google.
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.UseCookieAuthentication(new CookieAuthenticationOptions { AuthenticationType = "External" });
            // SMOKE_WITHOUT_GOOGLE=1 leaves the provider out, to see the MVC checks on packages where it fails.
            if (System.Environment.GetEnvironmentVariable("SMOKE_WITHOUT_GOOGLE") == "1") return;
            app.UseGoogleAuthentication(new GoogleOAuth2AuthenticationOptions
            {
                ClientId = "smoke-client-id",
                ClientSecret = "smoke-client-secret",
                SignInAsAuthenticationType = "External",
            });
            app.Map("/challenge", challenge => challenge.Run(context =>
            {
                context.Authentication.Challenge(new Microsoft.Owin.Security.AuthenticationProperties { RedirectUri = "/" }, "Google");
                context.Response.StatusCode = 401;
                return Task.FromResult(0);
            }));
        }
    }
}
