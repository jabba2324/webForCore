using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace WebForCoreOidc
{
    // A minimal OpenID Connect provider, enough for Katana's OpenIdConnectAuthenticationMiddleware as Entra ID (Azure AD)
    // serves it: discovery, JWKS, authorize (code, id_token, code id_token; query and form_post), token (authorization
    // code, client credentials), introspection (IdentityServer3's /connect/introspect) and end session. Tokens are RS256
    // JWTs signed with a key generated per run.
    //
    // A test chooses what the provider does wrong by adding wfc_test=<case> to the authorize request (the scenarios do,
    // as the browser that follows the application's redirect): tamper, expired, wrong_issuer, wrong_audience,
    // wrong_nonce, unknown_key, error. /test/access-token?kind=<case> issues access tokens for the bearer tests.
    public sealed class OidcTestProvider : IAsyncDisposable
    {
        public const string ClientId = "wfc-oidc-sample";
        public const string ClientSecret = "wfc-oidc-sample-secret";
        public const string ApiAudience = "api://wfc-oidc-sample";
        public const string ApiScope = "wfc.api";
        public const string ApiClientId = "wfc-api";
        public const string ApiClientSecret = "wfc-api-secret";
        public const string Subject = "8a8f1f3c-6c2b-4e5b-9d55-6b1e3c0f2a11";

        private readonly WebApplication _app;
        private readonly RSA _key = RSA.Create(2048);
        private readonly RSA _unknownKey = RSA.Create(2048);
        private readonly string _keyId;
        private readonly ConcurrentDictionary<string, CodeGrant> _codes = new();
        private readonly ConcurrentDictionary<string, bool> _referenceTokens = new();

        private OidcTestProvider(WebApplication app, string issuer)
        {
            _app = app;
            Issuer = issuer;
            _keyId = Base64Url(SHA256.HashData(_key.ExportSubjectPublicKeyInfo()))[..16];
        }

        public string Issuer { get; }

        // The last requests to the token and end-session endpoints, for the tests.
        public IReadOnlyDictionary<string, string>? LastTokenRequest { get; private set; }

        public IReadOnlyDictionary<string, string>? LastEndSessionRequest { get; private set; }

        public int TokenRequests;

        public static async Task<OidcTestProvider> StartAsync(string url)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls(url);
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var provider = new OidcTestProvider(app, url.TrimEnd('/'));
            provider.Map(app);
            await app.StartAsync();
            return provider;
        }

        public ValueTask DisposeAsync() => _app.DisposeAsync();

        public Task StopAsync() => _app.StopAsync();

        public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

        private void Map(WebApplication app)
        {
            app.MapGet("/.well-known/openid-configuration", () => Results.Json(new Dictionary<string, object>
            {
                ["issuer"] = Issuer,
                ["authorization_endpoint"] = Issuer + "/authorize",
                ["token_endpoint"] = Issuer + "/token",
                ["jwks_uri"] = Issuer + "/jwks",
                ["end_session_endpoint"] = Issuer + "/logout",
                ["introspection_endpoint"] = Issuer + "/connect/introspect",
                ["response_types_supported"] = new[] { "code", "id_token", "code id_token" },
                ["response_modes_supported"] = new[] { "query", "fragment", "form_post" },
                ["subject_types_supported"] = new[] { "pairwise" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256" },
                ["scopes_supported"] = new[] { "openid", "profile", "email", "offline_access", ApiScope },
                ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_post", "client_secret_basic" },
                ["claims_supported"] = new[] { "sub", "iss", "aud", "exp", "iat", "nbf", "nonce", "name", "preferred_username", "email", "oid", "tid" },
            }));

            app.MapGet("/jwks", () =>
            {
                var p = _key.ExportParameters(false);
                return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = _keyId, n = Base64Url(p.Modulus!), e = Base64Url(p.Exponent!) } } });
            });

            // Typed as delegates: a method group that takes an HttpContext and returns a Task would bind as a RequestDelegate,
            // and its IResult would be dropped.
            app.MapGet("/authorize", (Func<HttpContext, Task<IResult>>)Authorize);
            app.MapPost("/authorize", (Func<HttpContext, Task<IResult>>)Authorize);
            app.MapPost("/token", (Func<HttpContext, Task<IResult>>)Token);
            app.MapPost("/connect/introspect", (Func<HttpContext, Task<IResult>>)Introspect);
            app.MapGet("/logout", (HttpContext context) =>
            {
                LastEndSessionRequest = context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
                string? postLogout = context.Request.Query["post_logout_redirect_uri"];
                if (string.IsNullOrEmpty(postLogout)) return Results.Text("signed out", "text/plain");
                string? state = context.Request.Query["state"];
                return Results.Redirect(postLogout + (string.IsNullOrEmpty(state) ? "" : (postLogout.Contains('?') ? "&" : "?") + "state=" + Uri.EscapeDataString(state)));
            });

            // Access tokens for the bearer tests: kind = valid, expired, wrong_audience, wrong_issuer, tamper, unknown_key,
            // no_scope; reference = an opaque token that only introspection knows.
            app.MapGet("/test/access-token", (HttpContext context) =>
            {
                string kind = context.Request.Query["kind"].ToString() is { Length: > 0 } k ? k : "valid";
                if (kind == "reference")
                {
                    string reference = Base64Url(RandomNumberGenerator.GetBytes(32));
                    _referenceTokens[reference] = true;
                    return Results.Text(reference, "text/plain");
                }
                return Results.Text(AccessToken(kind, ApiClientId), "text/plain");
            });
        }

        private async Task<IResult> Authorize(HttpContext context)
        {
            var p = context.Request.HasFormContentType
                ? (await context.Request.ReadFormAsync()).ToDictionary(f => f.Key, f => f.Value.ToString())
                : context.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            string Get(string name) => p.TryGetValue(name, out var v) ? v : "";

            if (Get("client_id") != ClientId) return Results.BadRequest("unknown client_id");
            string redirectUri = Get("redirect_uri");
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out _)) return Results.BadRequest("redirect_uri is required");
            string responseType = Get("response_type");
            string responseMode = Get("response_mode") is { Length: > 0 } m ? m : responseType == "code" ? "query" : "fragment";
            string test = Get("wfc_test");

            var response = new List<KeyValuePair<string, string>>();
            if (test == "error")
            {
                response.Add(new("error", "access_denied"));
                response.Add(new("error_description", "The user denied the request (wfc_test=error)."));
            }
            else
            {
                string? code = null;
                if (responseType.Split(' ').Contains("code"))
                {
                    code = Base64Url(RandomNumberGenerator.GetBytes(32));
                    _codes[code] = new CodeGrant(redirectUri, Get("nonce"), test, Get("code_challenge"));
                    response.Add(new("code", code));
                }
                if (responseType.Split(' ').Contains("id_token"))
                {
                    response.Add(new("id_token", IdToken(Get("nonce"), test, code, null)));
                }
                response.Add(new("session_state", "wfc-session-state"));
            }
            if (p.ContainsKey("state")) response.Add(new("state", Get("state")));

            if (responseMode == "form_post")
            {
                var html = new StringBuilder("<html><head><title>Working...</title></head><body><form method=\"POST\" name=\"hiddenform\" action=\"")
                    .Append(WebUtility.HtmlEncode(redirectUri)).Append("\">");
                foreach (var (name, value) in response)
                    html.Append("<input type=\"hidden\" name=\"").Append(WebUtility.HtmlEncode(name)).Append("\" value=\"").Append(WebUtility.HtmlEncode(value)).Append("\" />");
                html.Append("<noscript><p>Script is disabled. Click Submit to continue.</p><input type=\"submit\" value=\"Submit\" /></noscript></form>")
                    .Append("<script language=\"javascript\">document.forms[0].submit();</script></body></html>");
                return Results.Content(html.ToString(), "text/html");
            }
            string encoded = string.Join("&", response.Select(r => Uri.EscapeDataString(r.Key) + "=" + Uri.EscapeDataString(r.Value)));
            return Results.Redirect(redirectUri + (responseMode == "query" ? (redirectUri.Contains('?') ? "&" : "?") : "#") + encoded);
        }

        private async Task<IResult> Token(HttpContext context)
        {
            Interlocked.Increment(ref TokenRequests);
            var form = (await context.Request.ReadFormAsync()).ToDictionary(f => f.Key, f => f.Value.ToString());
            LastTokenRequest = form;
            string Get(string name) => form.TryGetValue(name, out var v) ? v : "";
            var (clientId, clientSecret) = ClientCredentials(context, form);

            switch (Get("grant_type"))
            {
                case "authorization_code":
                    if (clientId != ClientId || clientSecret != ClientSecret) return TokenError("invalid_client", 401);
                    if (!_codes.TryRemove(Get("code"), out var grant)) return TokenError("invalid_grant", 400);
                    if (grant.RedirectUri != Get("redirect_uri")) return TokenError("invalid_grant", 400, "redirect_uri doesn't match the authorize request's");
                    if (grant.CodeChallenge.Length > 0 && Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(Get("code_verifier")))) != grant.CodeChallenge)
                        return TokenError("invalid_grant", 400, "code_verifier doesn't match");
                    string accessToken = AccessToken("valid", ClientId);
                    return Results.Json(new Dictionary<string, object>
                    {
                        ["token_type"] = "Bearer",
                        ["scope"] = "openid profile email offline_access " + ApiScope,
                        ["expires_in"] = 3600,
                        ["access_token"] = accessToken,
                        ["refresh_token"] = "wfc-refresh-" + Base64Url(RandomNumberGenerator.GetBytes(16)),
                        ["id_token"] = IdToken(grant.Nonce, "", null, accessToken),
                    });
                case "client_credentials":
                    if (clientId != ApiClientId || clientSecret != ApiClientSecret) return TokenError("invalid_client", 401);
                    return Results.Json(new Dictionary<string, object> { ["token_type"] = "Bearer", ["expires_in"] = 3600, ["access_token"] = AccessToken("valid", ApiClientId) });
                default:
                    return TokenError("unsupported_grant_type", 400);
            }
        }

        private async Task<IResult> Introspect(HttpContext context)
        {
            var form = (await context.Request.ReadFormAsync()).ToDictionary(f => f.Key, f => f.Value.ToString());
            var (clientId, clientSecret) = ClientCredentials(context, form);
            if (clientId != ApiClientId || clientSecret != ApiClientSecret) return Results.StatusCode(401);
            string token = form.TryGetValue("token", out var t) ? t : "";
            if (!_referenceTokens.ContainsKey(token)) return Results.Json(new { active = false });
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return Results.Json(new Dictionary<string, object>
            {
                ["active"] = true,
                ["iss"] = Issuer,
                ["sub"] = Subject,
                ["client_id"] = ApiClientId,
                ["scope"] = ApiScope,
                ["name"] = "Reference Token User",
                ["exp"] = now + 3600,
                ["nbf"] = now,
            });
        }

        private static (string, string) ClientCredentials(HttpContext context, Dictionary<string, string> form)
        {
            string authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                int colon = decoded.IndexOf(':');
                if (colon > 0) return (Uri.UnescapeDataString(decoded[..colon]), Uri.UnescapeDataString(decoded[(colon + 1)..]));
            }
            return (form.TryGetValue("client_id", out var id) ? id : "", form.TryGetValue("client_secret", out var secret) ? secret : "");
        }

        private static IResult TokenError(string error, int status, string? description = null) =>
            Results.Json(new Dictionary<string, string> { ["error"] = error, ["error_description"] = description ?? error }, statusCode: status);

        private string IdToken(string nonce, string test, string? code, string? accessToken)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = new JsonObject
            {
                ["aud"] = test == "wrong_audience" ? "some-other-client" : ClientId,
                ["iss"] = test == "wrong_issuer" ? "https://login.example.org/other-tenant/v2.0" : Issuer,
                ["iat"] = test == "expired" ? now - 7200 : now,
                ["nbf"] = test == "expired" ? now - 7200 : now,
                ["exp"] = test == "expired" ? now - 3600 : now + 3600,
                ["sub"] = Subject,
                ["oid"] = "00000000-0000-0000-0000-00000000a11c",
                ["tid"] = "00000000-0000-0000-0000-0000000000a1",
                ["name"] = "Alice Example",
                ["preferred_username"] = "alice@example.org",
                ["email"] = "alice@example.org",
                ["roles"] = new JsonArray("Reader", "Writer"),
                ["ver"] = "2.0",
            };
            if (nonce.Length > 0) claims["nonce"] = test == "wrong_nonce" ? "638000000000000000.bm90IHRoZSBub25jZSBpbiB0aGUgY29va2ll" : nonce;
            if (code != null) claims["c_hash"] = HalfHash(code);
            if (accessToken != null) claims["at_hash"] = HalfHash(accessToken);
            return Sign(claims, test);
        }

        private string AccessToken(string kind, string clientId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = new JsonObject
            {
                // Entra's API audience, and IdentityServer3's "<issuer>/resources".
                ["aud"] = kind == "wrong_audience" ? new JsonArray("api://someone-else") : new JsonArray(ApiAudience, Issuer + "/resources"),
                ["iss"] = kind == "wrong_issuer" ? "https://login.example.org/other-tenant/v2.0" : Issuer,
                ["iat"] = kind == "expired" ? now - 7200 : now,
                ["nbf"] = kind == "expired" ? now - 7200 : now,
                ["exp"] = kind == "expired" ? now - 3600 : now + 3600,
                ["sub"] = Subject,
                ["client_id"] = clientId,
                ["azp"] = clientId,
                ["name"] = "Alice Example",
                ["scope"] = kind == "no_scope" ? new JsonArray("openid") : new JsonArray(ApiScope),
                ["scp"] = kind == "no_scope" ? "openid" : ApiScope,
                ["roles"] = new JsonArray("Reader"),
            };
            return Sign(claims, kind);
        }

        private string Sign(JsonObject claims, string test)
        {
            var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = test == "unknown_key" ? "unknown-" + _keyId : _keyId };
            string signingInput = Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()));
            byte[] signature = (test == "unknown_key" ? _unknownKey : _key).SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (test == "tamper")
            {
                // The payload changed after signing: the signature no longer matches.
                claims["name"] = "Mallory Example";
                signingInput = signingInput[..signingInput.IndexOf('.')] + "." + Base64Url(Encoding.UTF8.GetBytes(claims.ToJsonString()));
            }
            return signingInput + "." + Base64Url(signature);
        }

        private static string HalfHash(string value)
        {
            byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(value));
            return Base64Url(hash[..(hash.Length / 2)]);
        }

        public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private sealed record CodeGrant(string RedirectUri, string Nonce, string Test, string CodeChallenge);
    }
}
