using System.Text.Json.Nodes;
using WebForCoreGate;
using WebForCoreOidc;
using Xunit;

namespace OidcGate
{
    // test/webForCore.Oidc.Sample (the Visual Studio "Work or School Accounts" template's OWIN startup, as a customer
    // application configures it for Entra ID) against test/webForCore.Oidc.Provider: what Katana 4.2.3 does on IIS, on
    // webForCore. The responses are Scenarios' normalised ones ({app}, {provider}, placeholders for random values); CI's
    // iis-compare job checks the same scenarios on IIS 10 with Microsoft's packages.
    public class OidcGateTests : IClassFixture<OidcGate>
    {
        private readonly OidcGate _gate;

        public OidcGateTests(OidcGate gate)
        {
            _gate = gate;
        }

        private int Status(string key) => (int)_gate.Response(key)["status"]!;

        private string Header(string key, string name) => (string)_gate.Response(key)["headers"]![name];

        private string Body(string key) => (string)_gate.Response(key)["body"];

        private void NoStoppedScenarios(string prefix)
        {
            var stopped = _gate.Responses.Where(r => r.Key.StartsWith(prefix, StringComparison.Ordinal) && r.Key.EndsWith(" stopped", StringComparison.Ordinal)).ToList();
            Assert.True(stopped.Count == 0, string.Join("\n", stopped.Select(s => s.Key + ": " + (string)s.Value["body"])) + "\n" + _gate.Output);
        }

        // An anonymous request for a page that <authorization> keeps from anonymous users: Katana turns the 401 into a
        // redirect to the authorize endpoint, with the parameters of the hybrid flow and form_post, the state in the
        // application's own StateDataFormat, the nonce in a cookie, and what RedirectToIdentityProvider added.
        [Fact]
        public void ChallengeRedirectsToTheAuthorizeEndpoint()
        {
            NoStoppedScenarios("oidc signin");
            const string key = "oidc signin 1 challenge";
            Assert.Equal(302, Status(key));
            Assert.Equal(
                "{provider}/authorize?client_id=wfc-oidc-sample&redirect_uri={app}/signin-oidc&response_type=code id_token" +
                "&scope=openid profile email offline_access wfc.api&state=OpenIdConnect.AuthenticationProperties=st1.{protected}" +
                "&response_mode=form_post&nonce={ticks}.{random}&domain_hint=example.org&x-client-SKU={IdentityModel platform}&x-client-ver={IdentityModel version}",
                Header(key, "Location"));
            // The nonce cookie: the StateDataFormat's protection of the nonce, base64-encoded, for 15 minutes (the
            // validator's NonceLifetime), SameSite=None (the form_post comes from the provider's site).
            Assert.Equal("OpenIdConnect.nonce.{hash}=base64(st1.{protected}); expires={future}; path=/; HttpOnly; SameSite=None",
                Header(key, "Set-Cookie"));
        }

        // The form_post back: the id_token is validated, the code redeemed, the user signed in with the application's
        // ticket format, the nonce cookie deleted, and the browser sent back to the page it asked for.
        [Fact]
        public void FormPostSignsInAndSetsTheCookie()
        {
            const string key = "oidc signin 2 form_post";
            Assert.Equal(302, Status(key));
            Assert.Equal("{app}/Secure/Claims.aspx", Header(key, "Location"));
            Assert.Equal(".AspNet.Cookies=wfc1.{protected}; path=/; HttpOnly | OpenIdConnect.nonce.{hash}=; expires={long ago}; path=/; HttpOnly; SameSite=None",
                Header(key, "Set-Cookie"));
            Assert.Contains("code=", (string)_gate.Response(key)["request"]!["body"]);
        }

        // HttpContext.User on the page: the id_token's claims (mapped as JwtSecurityTokenHandler maps them), and those
        // SecurityTokenValidated and AuthorizationCodeReceived added.
        [Fact]
        public void UserHasTheClaims()
        {
            string body = Body("oidc signin 3 after sign-in");
            Assert.Equal(200, Status("oidc signin 3 after sign-in"));
            Assert.Contains("name=Alice Example\n", body);
            Assert.Contains("authenticated=True\n", body);
            Assert.Contains("authenticationType=Cookies\n", body);
            Assert.Contains("claim http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier=" + OidcTestProvider.Subject + "\n", body);
            Assert.Contains("claim preferred_username=alice@example.org\n", body);
            Assert.Contains("claim iss={provider}\n", body);
            Assert.Contains("claim aud=wfc-oidc-sample\n", body);
            Assert.Contains("claim http://schemas.microsoft.com/ws/2008/06/identity/claims/role=Writer\n", body);
            Assert.Contains("claim wfc:validated=SecurityTokenValidated\n", body);
            Assert.Contains("claim http://schemas.microsoft.com/ws/2008/06/identity/claims/role=SampleRoleFromNotification\n", body);
            Assert.Contains("claim wfc:code=AuthorizationCodeReceived\n", body);
            Assert.DoesNotContain("wfc:redeemed", body);
        }

        // SaveTokens: what the token endpoint returned (the refresh token comes only from there, so the code was redeemed),
        // in the cookie's AuthenticationProperties, read back on a later request.
        [Fact]
        public void SaveTokensKeepsTheTokensInTheCookie()
        {
            const string key = "oidc signin 4 saved tokens";
            Assert.Equal(200, Status(key));
            Assert.Equal(
                ".expires={date}\n.issued={date}\n.redirect={app}/Secure/Claims.aspx\n.refresh=False\n.sessionState=wfc-session-state\n" +
                "OpenIdConnect.Code.RedirectUri={app}/signin-oidc\naccess_token=JWT sub=" + OidcTestProvider.Subject + "\nexpires_at={in an hour}\n" +
                "id_token=JWT sub=" + OidcTestProvider.Subject + "\nrefresh_token=wfc-refresh-{random}\ntoken_type=Bearer\n",
                Body(key));
            Assert.True(_gate.Provider.TokenRequests >= 2, "the token endpoint wasn't called for each sign-in");
        }

        // AuthorizationCodeReceived redeeming the code itself (as ADAL and MSAL applications do) and handing Katana the
        // tokens with HandleCodeRedemption.
        [Fact]
        public void AuthorizationCodeReceivedCanRedeemTheCode()
        {
            NoStoppedScenarios("oidc signin-manual");
            Assert.Equal(302, Status("oidc signin-manual 1 challenge"));
            // /login's challenge asked for a relative RedirectUri; the Claims.aspx challenge got the absolute URL Katana built.
            Assert.Equal("/Secure/Claims.aspx", Header("oidc signin-manual 2 form_post", "Location"));
            string body = Body("oidc signin-manual 3 after sign-in");
            Assert.Contains("claim wfc:redeemed=manually\n", body);
            Assert.Contains("claim wfc:code=AuthorizationCodeReceived\n", body);
            Assert.Contains("access_token=JWT sub=", Body("oidc signin-manual 4 saved tokens"));
        }

        // Sign-out: the cookie deleted and the browser sent to the end_session endpoint, with the id_token_hint
        // RedirectToIdentityProvider read from the saved tokens; afterwards the page challenges again.
        [Fact]
        public void SignOutRedirectsToEndSession()
        {
            const string key = "oidc signin 5 sign-out";
            Assert.Equal(302, Status(key));
            Assert.Equal("{provider}/logout?post_logout_redirect_uri={app}/&id_token_hint={id_token_hint}&x-client-SKU={IdentityModel platform}&x-client-ver={IdentityModel version}",
                Header(key, "Location"));
            Assert.Equal(".AspNet.Cookies=; expires={long ago}; path=/; HttpOnly", Header(key, "Set-Cookie"));
            Assert.Equal(3, _gate.Provider.LastEndSessionRequest!["id_token_hint"].Split('.').Length);
            Assert.Equal(200, Status("oidc signin 6 after sign-out"));
            Assert.Equal(302, Status("oidc signin 7 signed out"));
            Assert.StartsWith("{provider}/authorize?", Header("oidc signin 7 signed out", "Location"));
        }

        // A response Katana rejects: AuthenticationFailed's HandleResponse sends the browser to the error page with the
        // exception's type, and nobody is signed in.
        [Theory]
        [InlineData("tamper", "SecurityTokenInvalidSignatureException")]
        [InlineData("expired", "SecurityTokenExpiredException")]
        [InlineData("wrong_issuer", "SecurityTokenInvalidIssuerException")]
        [InlineData("wrong_audience", "SecurityTokenInvalidAudienceException")]
        [InlineData("wrong_nonce", "OpenIdConnectProtocolInvalidNonceException")]
        [InlineData("unknown_key", "SecurityTokenSignatureKeyNotFoundException")]
        [InlineData("error", "OpenIdConnectProtocolException")]
        [InlineData("nonce cookie missing", "OpenIdConnectProtocolInvalidNonceException")]
        public void RejectedResponsesFailTheSignIn(string failure, string exception)
        {
            NoStoppedScenarios("oidc failure " + failure);
            string key = "oidc failure " + failure + " 1 form_post";
            Assert.Equal(302, Status(key));
            Assert.Equal("/Error.aspx?type=" + exception, Header(key, "Location"));
            Assert.DoesNotContain(".AspNet.Cookies=wfc1", Header(key, "Set-Cookie") ?? "");
            Assert.Contains("Sign-in failed: " + exception, Body("oidc failure " + failure + " 2 error page"));
        }

        // AuthenticationFailed's SkipToNextMiddleware: the form_post goes on to the application, unauthenticated.
        [Fact]
        public void AuthenticationFailedCanSkipToTheNextMiddleware()
        {
            const string key = "oidc failure tamper onfail=skip 1 form_post";
            Assert.Equal(200, Status(key));
            Assert.Equal("The sign-in response reached the application: authenticated=False", Body(key));
        }

        // AuthenticationFailed doing neither: the exception fails the request, as on IIS.
        [Fact]
        public void AnUnhandledFailureIsAServerError()
        {
            const string key = "oidc failure tamper onfail=throw 1 form_post";
            Assert.Equal(500, Status(key));
            Assert.Equal("ASP.NET error page: Microsoft.IdentityModel.Tokens.SecurityTokenInvalidSignatureException", Body(key));
        }

        // A state the application didn't protect isn't Katana's message: it is ignored, and the request goes on.
        [Fact]
        public void ATamperedStateIsIgnored()
        {
            const string key = "oidc failure state tampered 1 form_post";
            Assert.Equal(200, Status(key));
            Assert.Equal("The sign-in response reached the application: authenticated=False", Body(key));
        }

        // Microsoft.Owin.Security.Jwt on the Web API: a valid access token is accepted, any other is a 401 with a Bearer
        // challenge (never a redirect to sign in).
        [Theory]
        [InlineData("valid", 200)]
        [InlineData("expired", 401)]
        [InlineData("wrong_audience", 401)]
        [InlineData("wrong_issuer", 401)]
        [InlineData("tamper", 401)]
        [InlineData("unknown_key", 401)]
        [InlineData("none", 401)]
        public void JwtBearer(string token, int status)
        {
            string key = "oidc bearer jwt " + token + " 1 GET";
            Assert.Equal(status, Status(key));
            if (status == 200) Assert.StartsWith("\"name=; authenticationType=JWT; aud=api://wfc-oidc-sample; aud={provider}/resources; client_id=wfc-api; scope=wfc.api", Body(key));
            else Assert.Equal("Bearer", Header(key, "WWW-Authenticate"));
        }

        // IdentityServer3.Contrib.AccessTokenValidation 4.1.16, the net45 package as it is: local JWT validation against
        // the provider's discovery document and JWKS, its scope requirement, and reference tokens through introspection.
        [Theory]
        [InlineData("valid", 200)]
        [InlineData("reference", 200)]
        [InlineData("no_scope", 403)]
        [InlineData("expired", 401)]
        [InlineData("wrong_audience", 401)]
        [InlineData("tamper", 401)]
        [InlineData("unknown_reference", 401)]
        [InlineData("none", 401)]
        public void IdentityServer3AccessTokenValidation(string token, int status)
        {
            string key = "oidc bearer idsrv3 " + token + " 1 GET";
            Assert.Equal(status, Status(key));
            if (token == "valid") Assert.StartsWith("\"name=Alice Example; authenticationType=JWT;", Body(key));
            if (token == "reference") Assert.StartsWith("\"name=Reference Token User; authenticationType=Bearer;", Body(key));
        }

        // Katana's OpenID Connect middleware is built against the IdentityModel that CoreWCF already brings (8.x): one
        // version of every IdentityModel package in the application, and the authorize request says so in IdentityModel's
        // telemetry parameters (IIS with Microsoft's package sends 5.x and a .NET Framework platform).
        [Fact]
        public async Task OneIdentityModelVersion()
        {
            string deps = File.ReadAllText(Path.Combine(WebForCoreHost.Repository(Path.Combine("test", "webForCore.Oidc.Sample")),
                "test", "webForCore.Oidc.Sample", "bin_dotnet", "webForCore.Oidc.Sample.deps.json"));
            var versions = System.Text.RegularExpressions.Regex.Matches(deps, "\"(?:Microsoft|System)\\.IdentityModel[A-Za-z.]*/([0-9.]+)\"")
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            string version = Assert.Single(versions);
            Assert.StartsWith("8.", version);

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            using var response = await client.GetAsync(new Uri(_gate.AppBase, "/login"));
            Assert.Contains("&x-client-SKU=ID_NET10_0&x-client-ver=" + version + ".0", response.Headers.Location!.AbsoluteUri);
        }

        // Katana's default TicketDataFormat on System.Web (the machine key's data protection): a cookie it wrote reads back.
        [Fact]
        public void DefaultTicketFormatRoundTrips()
        {
            Assert.Equal(".wfc.Default={protected}; path=/; HttpOnly", Header("oidc default ticket format 1 sign-in", "Set-Cookie"));
            Assert.Equal("default-format-user default", Body("oidc default ticket format 2 read back"));
        }
    }
}
