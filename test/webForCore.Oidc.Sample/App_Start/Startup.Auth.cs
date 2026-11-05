using System;
using System.Collections.Generic;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using IdentityServer3.AccessTokenValidation;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Owin;
using Microsoft.Owin.Extensions;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.DataProtection;
using Microsoft.Owin.Security.Jwt;
using Microsoft.Owin.Security.Notifications;
using Microsoft.Owin.Security.OpenIdConnect;
using Owin;

namespace OidcSample
{
    // The Visual Studio "Work or School Accounts" (Azure AD / Entra ID) template's Startup.Auth.cs, with what a customer
    // application adds to it: the hybrid flow (code id_token) with form_post, the code redeemed at the token endpoint,
    // SaveTokens, notifications that change the outcome, its own ticket and state formats, and bearer tokens for its
    // Web API through Microsoft.Owin.Security.Jwt and IdentityServer3.Contrib.AccessTokenValidation. The same file runs
    // on webForCore (test/webForCore.Oidc.Sample) and on IIS 10 with Microsoft's packages (test/webForCore.Oidc.Sample.Iis).
    public partial class Startup
    {
        // ida:* as the template has them in Web.config's <appSettings>. The gate passes its ports in the environment
        // (WFC_IDA_AUTHORITY, ...), since they change every run.
        private static readonly string ClientId = Setting("ida:ClientId");
        private static readonly string ClientSecret = Setting("ida:ClientSecret");
        private static readonly string Authority = Setting("ida:Authority");
        private static readonly string RedirectUri = Setting("ida:RedirectUri");
        private static readonly string PostLogoutRedirectUri = Setting("ida:PostLogoutRedirectUri");
        private static readonly string ApiAudience = Setting("ida:ApiAudience");

        public static string Setting(string name) =>
            Environment.GetEnvironmentVariable("WFC_" + name.Replace(':', '_').ToUpperInvariant()) ?? ConfigurationManager.AppSettings[name];

        public void ConfigureAuth(IAppBuilder app)
        {
            // The Web API: bearer tokens only, never a redirect to sign in.
            app.Map("/api", api =>
            {
                api.UseJwtBearerAuthentication(new JwtBearerAuthenticationOptions
                {
                    AuthenticationMode = AuthenticationMode.Active,
                    AllowedAudiences = new[] { ApiAudience },
                    // As Microsoft's Azure AD Web API samples do: keys and issuer from the discovery document. (With
                    // TokenValidationParameters set, Katana would use those alone and ignore these.)
                    IssuerSecurityKeyProviders = new IIssuerSecurityKeyProvider[]
                    {
                        new OpenIdConnectCachingSecurityTokenProvider(Authority.TrimEnd('/') + "/.well-known/openid-configuration"),
                    },
                });
                api.UseWebApi(WebApiConfig.Create());
            });

            // The same API behind IdentityServer3's access token validation (the original net45 package).
            app.Map("/idsrv-api", api =>
            {
                api.UseIdentityServerBearerTokenAuthentication(new IdentityServerBearerTokenAuthenticationOptions
                {
                    Authority = Authority,
                    RequireHttps = false,
                    RequiredScopes = new[] { "wfc.api" },
                    ValidationMode = ValidationMode.Both,
                    ClientId = "wfc-api",
                    ClientSecret = "wfc-api-secret",
                    NameClaimType = "name",
                });
                api.UseWebApi(WebApiConfig.Create());
            });

            // A cookie with Katana's default ticket format: on System.Web, the machine key protects it.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = "Default",
                AuthenticationMode = AuthenticationMode.Passive,
                CookieName = ".wfc.Default",
            });

            app.SetDefaultSignInAsAuthenticationType(CookieAuthenticationDefaults.AuthenticationType);

            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                TicketDataFormat = new SampleTicketDataFormat(app.CreateDataProtector("OidcSample", "Cookies", "v1")),
            });

            app.UseOpenIdConnectAuthentication(new OpenIdConnectAuthenticationOptions
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                Authority = Authority,
                RedirectUri = RedirectUri,
                PostLogoutRedirectUri = PostLogoutRedirectUri,
                ResponseType = OpenIdConnectResponseType.CodeIdToken,
                ResponseMode = OpenIdConnectResponseMode.FormPost,
                Scope = "openid profile email offline_access wfc.api",
                RedeemCode = true,
                SaveTokens = true,
                // The test provider is plain HTTP; Entra ID is HTTPS and needs no setting.
                RequireHttpsMetadata = false,
                StateDataFormat = new SampleStateDataFormat(app.CreateDataProtector("OidcSample", "State", "v1")),
                TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "roles",
                },
                Notifications = new OpenIdConnectAuthenticationNotifications
                {
                    RedirectToIdentityProvider = OnRedirectToIdentityProvider,
                    SecurityTokenValidated = OnSecurityTokenValidated,
                    AuthorizationCodeReceived = OnAuthorizationCodeReceived,
                    TokenResponseReceived = n =>
                    {
                        n.OwinContext.Set("wfc:token-response", n.TokenEndpointResponse.TokenType);
                        return Task.FromResult(0);
                    },
                    AuthenticationFailed = OnAuthenticationFailed,
                },
            });

            // The authentication middleware runs in System.Web's AuthenticateRequest, before <authorization> (Secure/
            // denies anonymous users) decides: otherwise the OWIN pipeline runs at PreHandlerExecute, which a request
            // that <authorization> refuses never reaches, and the 401 isn't turned into a sign-in.
            app.UseStageMarker(PipelineStage.Authenticate);

            // Where the form_post arrives when AuthenticationFailed skips to the next middleware.
            app.Map("/signin-oidc", callback => callback.Run(context =>
            {
                context.Response.ContentType = "text/plain";
                return context.Response.WriteAsync("The sign-in response reached the application: authenticated=" +
                    (context.Authentication.User != null && context.Authentication.User.Identity.IsAuthenticated));
            }));

            app.Map("/login", login => login.Run(context =>
            {
                var properties = new AuthenticationProperties { RedirectUri = "/Secure/Claims.aspx" };
                if (context.Request.Query["manual"] == "1") properties.Dictionary["wfc:manual-redemption"] = "1";
                context.Authentication.Challenge(properties, OpenIdConnectAuthenticationDefaults.AuthenticationType);
                context.Response.StatusCode = 401;
                return Task.FromResult(0);
            }));

            app.Map("/logout", logout => logout.Run(context =>
            {
                context.Authentication.SignOut(
                    new AuthenticationProperties { RedirectUri = PostLogoutRedirectUri },
                    OpenIdConnectAuthenticationDefaults.AuthenticationType, CookieAuthenticationDefaults.AuthenticationType);
                return Task.FromResult(0);
            }));

            // The tokens SaveTokens kept in the cookie's AuthenticationProperties, read back on a later request.
            app.Map("/tokens", tokens => tokens.Run(async context =>
            {
                var result = await context.Authentication.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationType);
                context.Response.ContentType = "text/plain";
                if (result == null)
                {
                    await context.Response.WriteAsync("not signed in");
                    return;
                }
                foreach (var item in result.Properties.Dictionary.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    await context.Response.WriteAsync(item.Key + "=" + Describe(item.Key, item.Value) + "\n");
                }
            }));

            // Katana's default ticket format, round-tripped through a cookie.
            app.Map("/default-cookie/signin", signin => signin.Run(context =>
            {
                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "default-format-user"), new Claim("wfc:format", "default") }, "Default");
                context.Authentication.SignIn(new AuthenticationProperties(), identity);
                context.Response.ContentType = "text/plain";
                return context.Response.WriteAsync("signed in");
            }));
            app.Map("/default-cookie/whoami", whoami => whoami.Run(async context =>
            {
                var result = await context.Authentication.AuthenticateAsync("Default");
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(result == null ? "anonymous" : result.Identity.Name + " " + result.Identity.FindFirst("wfc:format").Value);
            }));
        }

        private static string Describe(string key, string value)
        {
            switch (key)
            {
                case "id_token":
                case "access_token":
                    // A JWT: its claims are checked elsewhere; here, that it is one, with the subject.
                    var jwt = new JwtSecurityToken(value);
                    return "JWT sub=" + jwt.Subject;
                case "refresh_token":
                    return value.StartsWith("wfc-refresh-", StringComparison.Ordinal) ? "wfc-refresh-{random}" : value;
                case "expires_at":
                    return DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind) > DateTime.UtcNow.AddMinutes(50) ? "{in an hour}" : value;
                case ".issued":
                case ".expires":
                    return "{date}";
                default:
                    return value;
            }
        }

        private static async Task OnRedirectToIdentityProvider(RedirectToIdentityProviderNotification<OpenIdConnectMessage, OpenIdConnectAuthenticationOptions> n)
        {
            if (n.ProtocolMessage.RequestType == OpenIdConnectRequestType.Authentication)
            {
                // As applications add a domain or login hint for Entra ID.
                n.ProtocolMessage.DomainHint = "example.org";
            }
            else if (n.ProtocolMessage.RequestType == OpenIdConnectRequestType.Logout)
            {
                // Katana doesn't send id_token_hint itself; the template's applications add the one SaveTokens kept.
                var result = await n.OwinContext.Authentication.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationType);
                string idToken;
                if (result != null && result.Properties.Dictionary.TryGetValue("id_token", out idToken))
                {
                    n.ProtocolMessage.IdTokenHint = idToken;
                }
            }
        }

        private static Task OnSecurityTokenValidated(SecurityTokenValidatedNotification<OpenIdConnectMessage, OpenIdConnectAuthenticationOptions> n)
        {
            var identity = n.AuthenticationTicket.Identity;
            identity.AddClaim(new Claim("wfc:validated", "SecurityTokenValidated"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "SampleRoleFromNotification"));
            return Task.FromResult(0);
        }

        private static async Task OnAuthorizationCodeReceived(AuthorizationCodeReceivedNotification n)
        {
            n.AuthenticationTicket.Identity.AddClaim(new Claim("wfc:code", "AuthorizationCodeReceived"));
            string manual;
            if (n.AuthenticationTicket.Properties.Dictionary.TryGetValue("wfc:manual-redemption", out manual) && manual == "1")
            {
                // As applications that redeem the code themselves (ADAL, MSAL) do.
                var configuration = await n.Options.ConfigurationManager.GetConfigurationAsync(n.Request.CallCancelled);
                using (var client = new HttpClient())
                {
                    var response = await client.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(n.TokenEndpointRequest.Parameters));
                    var message = new OpenIdConnectMessage(await response.Content.ReadAsStringAsync());
                    n.AuthenticationTicket.Identity.AddClaim(new Claim("wfc:redeemed", "manually"));
                    n.HandleCodeRedemption(message.AccessToken, message.IdToken);
                }
            }
        }

        private static Task OnAuthenticationFailed(AuthenticationFailedNotification<OpenIdConnectMessage, OpenIdConnectAuthenticationOptions> n)
        {
            // ?onfail=skip and ?onfail=throw on the form_post's URL choose the other outcomes.
            string onFail = n.Request.Query["onfail"];
            if (onFail == "skip")
            {
                n.SkipToNextMiddleware();
            }
            else if (onFail != "throw")
            {
                // The template's handler.
                n.HandleResponse();
                n.Response.Redirect("/Error.aspx?type=" + Uri.EscapeDataString(n.Exception.GetType().Name));
            }
            return Task.FromResult(0);
        }
    }
}
