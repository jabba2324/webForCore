using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Google;
using Microsoft.Owin.Security.OpenIdConnect;
using Owin;

[assembly: OwinStartup(typeof(SmokeMvc.Startup))]

namespace SmokeMvc
{
    // As the Visual Studio template's Startup.Auth.cs with Google sign-in (OpenOrderFramework, SocialGoal), on the
    // original Katana 3.0.1 package (#18). /challenge asks the Google middleware to sign in, which redirects to Google.
    // /oidc/challenge does the same with webForCore's OpenID Connect middleware, configured as for Entra ID (with the
    // provider's configuration given, so that nothing is fetched).
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.Map("/oidc", oidc =>
            {
                oidc.UseCookieAuthentication(new CookieAuthenticationOptions { AuthenticationType = "OidcCookies" });
                oidc.UseOpenIdConnectAuthentication(new OpenIdConnectAuthenticationOptions
                {
                    ClientId = "smoke-client-id",
                    SignInAsAuthenticationType = "OidcCookies",
                    RedirectUri = "https://smoke.example.org/signin-oidc",
                    ResponseType = OpenIdConnectResponseType.CodeIdToken,
                    ResponseMode = OpenIdConnectResponseMode.FormPost,
                    Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://login.microsoftonline.com/smoke-tenant/v2.0",
                        AuthorizationEndpoint = "https://login.microsoftonline.com/smoke-tenant/oauth2/v2.0/authorize",
                    },
                });
                oidc.Map("/challenge", challenge => challenge.Run(context =>
                {
                    context.Authentication.Challenge(new AuthenticationProperties { RedirectUri = "/" }, OpenIdConnectAuthenticationDefaults.AuthenticationType);
                    context.Response.StatusCode = 401;
                    return Task.FromResult(0);
                }));
            });

            // #195: SignalR's hub proxy lists the hubs it found in the application's assemblies.
            app.MapSignalR();
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
