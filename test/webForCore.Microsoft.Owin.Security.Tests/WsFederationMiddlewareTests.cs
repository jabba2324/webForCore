// webForCore: added to the lifted Katana tests (not from aspnet/AspNetKatana), which have no unit tests of the
// WS-Federation middleware (its functional test deploys to IIS Express or HttpListener).
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.IdentityModel.Protocols.WsFederation;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Tokens.Saml2;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.WsFederation;
using Microsoft.Owin.Testing;
using Owin;
using Xunit;

namespace Microsoft.Owin.Security.Tests.WsFederation
{
    // Katana's WS-Federation middleware, built against IdentityModel 8 (as Bonobo Git Server and ADFS-era applications
    // use it): the challenge redirects to the STS with wtrealm, wa and wctx, and a SAML 2.0 token the STS signed, posted
    // back in wresult, signs the user in; one signed by another key, or for another realm, doesn't.
    public class WsFederationMiddlewareTests
    {
        private const string Realm = "urn:webforcore:wsfed-test";
        private const string Issuer = "https://sts.example.org/adfs/services/trust";
        private const string TokenEndpoint = "https://sts.example.org/adfs/ls/";
        private static readonly RsaSecurityKey StsKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "sts" };

        [Fact]
        public async Task ChallengeRedirectsToTheSts()
        {
            using TestServer server = CreateServer();
            HttpResponseMessage response = await server.HttpClient.GetAsync("http://example.org/challenge");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location.AbsoluteUri;
            Assert.StartsWith(TokenEndpoint + "?", location);
            Assert.Contains("wtrealm=" + Uri.EscapeDataString(Realm), location);
            Assert.Contains("wa=wsignin1.0", location);
            Assert.Contains("wctx=WsFedOwinState%3D", location);
        }

        [Fact]
        public async Task SignedTokenSignsTheUserIn()
        {
            using TestServer server = CreateServer();
            string wctx = await ChallengeWctxAsync(server);

            HttpResponseMessage response = await PostTokenAsync(server, wctx, CreateToken(StsKey, Realm));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/signed-in", response.Headers.Location.OriginalString);
            string cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNet.Cookies=", StringComparison.Ordinal));

            HttpRequestMessage me = new HttpRequestMessage(HttpMethod.Get, "http://example.org/me");
            me.Headers.Add("Cookie", cookie.Split(';')[0]);
            HttpResponseMessage user = await server.HttpClient.SendAsync(me);
            Assert.Equal("alice@example.org Cookies", await user.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TokenSignedByAnotherKeyIsRejected()
        {
            using TestServer server = CreateServer();
            string wctx = await ChallengeWctxAsync(server);

            var other = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "other" };
            await Assert.ThrowsAnyAsync<SecurityTokenException>(() => PostTokenAsync(server, wctx, CreateToken(other, Realm)));
        }

        [Fact]
        public async Task TokenForAnotherRealmIsRejected()
        {
            using TestServer server = CreateServer();
            string wctx = await ChallengeWctxAsync(server);

            await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(() => PostTokenAsync(server, wctx, CreateToken(StsKey, "urn:someone-else")));
        }

        private static async Task<string> ChallengeWctxAsync(TestServer server)
        {
            HttpResponseMessage challenge = await server.HttpClient.GetAsync("http://example.org/challenge");
            string query = challenge.Headers.Location.Query.TrimStart('?');
            return query.Split('&').Select(p => p.Split('=')).Where(p => p[0] == "wctx").Select(p => Uri.UnescapeDataString(p[1])).Single();
        }

        private static Task<HttpResponseMessage> PostTokenAsync(TestServer server, string wctx, string token)
        {
            string wresult =
                "<t:RequestSecurityTokenResponse xmlns:t=\"http://schemas.xmlsoap.org/ws/2005/02/trust\">" +
                "<t:RequestedSecurityToken>" + token + "</t:RequestedSecurityToken>" +
                "</t:RequestSecurityTokenResponse>";
            return server.HttpClient.PostAsync("http://example.org/signin-wsfed", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["wa"] = "wsignin1.0",
                ["wresult"] = wresult,
                ["wctx"] = wctx,
            }));
        }

        private static string CreateToken(SecurityKey key, string audience)
        {
            var handler = new Saml2SecurityTokenHandler();
            SecurityToken token = handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = audience,
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "alice@example.org"), new Claim(ClaimTypes.NameIdentifier, "alice") }),
                IssuedAt = DateTime.UtcNow,
                NotBefore = DateTime.UtcNow,
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256Signature, SecurityAlgorithms.Sha256Digest),
            });
            return handler.WriteToken(token);
        }

        private static TestServer CreateServer()
        {
            return TestServer.Create(app =>
            {
                // TestServer has no data protection and DPAPI is Windows only (TestDataProtectionProvider.cs).
                Microsoft.Owin.Security.DataProtection.AppBuilderExtensions.SetDataProtectionProvider(app, new TestDataProtectionProvider());
                app.SetDefaultSignInAsAuthenticationType(CookieAuthenticationDefaults.AuthenticationType);
                app.UseCookieAuthentication(new CookieAuthenticationOptions());
                var configuration = new WsFederationConfiguration { Issuer = Issuer, TokenEndpoint = TokenEndpoint };
                configuration.SigningKeys.Add(StsKey);
                app.UseWsFederationAuthentication(new WsFederationAuthenticationOptions
                {
                    Wtrealm = Realm,
                    Configuration = configuration,
                    CallbackPath = new PathString("/signin-wsfed"),
                });
                app.Map("/challenge", challenge => challenge.Run(context =>
                {
                    context.Authentication.Challenge(new AuthenticationProperties { RedirectUri = "/signed-in" }, WsFederationAuthenticationDefaults.AuthenticationType);
                    context.Response.StatusCode = 401;
                    return Task.FromResult(0);
                }));
                app.Map("/me", me => me.Run(context =>
                {
                    ClaimsIdentity identity = context.Authentication.User?.Identity as ClaimsIdentity;
                    return context.Response.WriteAsync(identity == null ? "anonymous" : identity.Name + " " + identity.AuthenticationType);
                }));
            });
        }
    }
}
