using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WebForCoreOidc
{
    // The OpenID Connect gate's scenarios: a browser signing in to test/webForCore.Oidc.Sample (or its IIS build) through
    // OidcTestProvider, failing to, signing out, and calling its Web API with bearer tokens. Every response of the
    // application is recorded, normalised so that the same scenario on webForCore and on IIS 10 gives the same entry
    // (random values, ports and dates become placeholders), in tools/ci/iis_compare.cs's format: key -> {request, status,
    // headers, body}. OidcGateTests asserts on the entries; CI's iis-compare job diffs IIS's against webForCore's.
    public static class Scenarios
    {
        public static readonly string[] FailureCases = { "tamper", "expired", "wrong_issuer", "wrong_audience", "wrong_nonce", "unknown_key", "error" };
        public static readonly string[] BearerCases = { "valid", "expired", "wrong_audience", "wrong_issuer", "tamper", "unknown_key", "none" };
        public static readonly string[] IdentityServerCases = { "valid", "expired", "wrong_audience", "tamper", "no_scope", "reference", "unknown_reference", "none" };

        public static async Task<SortedDictionary<string, JsonObject>> RunAllAsync(Uri app, Uri provider, TextWriter? log = null)
        {
            var results = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
            await WarmUpAsync(app);
            await Run(results, "signin", () => SignInAndOutAsync(app, provider, results, manualRedemption: false));
            await Run(results, "signin-manual", () => SignInAndOutAsync(app, provider, results, manualRedemption: true));
            foreach (string failure in FailureCases) await Run(results, "failure " + failure, () => FailedSignInAsync(app, provider, results, failure, onFail: null));
            await Run(results, "failure tamper onfail=skip", () => FailedSignInAsync(app, provider, results, "tamper", onFail: "skip"));
            await Run(results, "failure tamper onfail=throw", () => FailedSignInAsync(app, provider, results, "tamper", onFail: "throw"));
            await Run(results, "failure nonce cookie missing", () => NonceCookieMissingAsync(app, provider, results));
            await Run(results, "failure state tampered", () => StateTamperedAsync(app, provider, results));
            foreach (string kind in BearerCases) await Run(results, "bearer jwt " + kind, () => BearerAsync(app, provider, results, "/api/me", "jwt", kind));
            foreach (string kind in IdentityServerCases) await Run(results, "bearer idsrv3 " + kind, () => BearerAsync(app, provider, results, "/idsrv-api/me", "idsrv3", kind));
            await Run(results, "default ticket format", () => DefaultTicketFormatAsync(app, provider, results));
            if (log != null)
                foreach (var (key, entry) in results) log.WriteLine($"{(int)entry["status"]!} {key}");
            return results;
        }

        // A scenario that can't go on (the application answered something a browser couldn't follow) is recorded as
        // such, and the others still run.
        private static async Task Run(SortedDictionary<string, JsonObject> results, string scenario, Func<Task> run)
        {
            try
            {
                await run();
            }
            catch (Exception e) when (e is InvalidOperationException or HttpRequestException or NullReferenceException or TaskCanceledException)
            {
                results[$"oidc {scenario} stopped"] = new JsonObject
                {
                    ["request"] = new JsonObject { ["method"] = "GET", ["url"] = "http://scenario.invalid/" },
                    ["status"] = 0,
                    ["headers"] = new JsonObject(),
                    ["body"] = e.GetType().Name + ": " + e.Message,
                };
            }
        }

        public static string ToJson(SortedDictionary<string, JsonObject> results)
        {
            var all = new JsonObject();
            foreach (var (key, value) in results) all[key] = value.DeepClone();
            return all.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        // The first request starts the application (Application_Start, the OWIN startup).
        private static async Task WarmUpAsync(Uri app)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            for (int i = 0; ; i++)
            {
                try
                {
                    using var response = await client.GetAsync(new Uri(app, "/Default.aspx"));
                    return;
                }
                catch (HttpRequestException) when (i < 120)
                {
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task SignInAndOutAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results, bool manualRedemption)
        {
            var browser = new Browser(app, provider, results, manualRedemption ? "signin-manual" : "signin");
            // The challenge: an anonymous request for a page that needs a user (manual redemption: /login?manual=1).
            var challenge = await browser.SendAsync(HttpMethod.Get, manualRedemption ? "/login?manual=1" : "/Secure/Claims.aspx", "challenge");
            var form = await browser.AuthorizeAsync(challenge, null);
            var signedIn = await browser.PostFormAsync(form, null, "form_post");
            await browser.SendAsync(HttpMethod.Get, signedIn.Headers.Location!.OriginalString, "after sign-in");
            await browser.SendAsync(HttpMethod.Get, "/tokens", "saved tokens");
            if (manualRedemption) return;
            var signOut = await browser.SendAsync(HttpMethod.Get, "/logout", "sign-out");
            // The provider sends the browser back to post_logout_redirect_uri.
            using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }))
            using (var endSession = await client.GetAsync(signOut.Headers.Location))
            {
                await browser.SendAsync(HttpMethod.Get, endSession.Headers.Location!.OriginalString, "after sign-out");
            }
            await browser.SendAsync(HttpMethod.Get, "/Secure/Claims.aspx", "signed out");
        }

        private static async Task FailedSignInAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results, string failure, string? onFail)
        {
            var browser = new Browser(app, provider, results, "failure " + failure + (onFail == null ? "" : " onfail=" + onFail));
            var challenge = await browser.SendAsync(HttpMethod.Get, "/login", "challenge", record: false);
            var form = await browser.AuthorizeAsync(challenge, failure);
            var response = await browser.PostFormAsync(form, onFail == null ? null : "onfail=" + onFail, "form_post");
            if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("Error.aspx"))
                await browser.SendAsync(HttpMethod.Get, response.Headers.Location.OriginalString, "error page");
        }

        // A valid response whose nonce cookie the browser didn't keep (as a browser that drops a SameSite=None cookie
        // without Secure does).
        private static async Task NonceCookieMissingAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results)
        {
            var browser = new Browser(app, provider, results, "failure nonce cookie missing");
            var challenge = await browser.SendAsync(HttpMethod.Get, "/login", "challenge", record: false);
            var form = await browser.AuthorizeAsync(challenge, null);
            browser.Cookies.Clear();
            var response = await browser.PostFormAsync(form, null, "form_post");
            if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("Error.aspx"))
                await browser.SendAsync(HttpMethod.Get, response.Headers.Location.OriginalString, "error page");
        }

        // A state the application didn't protect: Katana ignores the message, and the request goes on to the application.
        private static async Task StateTamperedAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results)
        {
            var browser = new Browser(app, provider, results, "failure state tampered");
            var challenge = await browser.SendAsync(HttpMethod.Get, "/login", "challenge", record: false);
            var form = await browser.AuthorizeAsync(challenge, null);
            int state = form.Fields.FindIndex(f => f.Key == "state");
            form.Fields[state] = new("state", form.Fields[state].Value[..^4] + "AAAA");
            await browser.PostFormAsync(form, null, "form_post");
        }

        private static async Task BearerAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results, string path, string name, string kind)
        {
            var browser = new Browser(app, provider, results, "bearer " + name + " " + kind);
            string? token = null;
            if (kind != "none")
            {
                using var client = new HttpClient();
                string tokenKind = kind == "unknown_reference" ? "reference" : kind;
                token = await client.GetStringAsync(new Uri(provider, "/test/access-token?kind=" + tokenKind));
                if (kind == "unknown_reference") token = token[..^4] + "AAAA";
            }
            await browser.SendAsync(HttpMethod.Get, path, "GET", bearer: token);
        }

        // Katana's default ticket format (the machine key on System.Web), through a cookie and back.
        private static async Task DefaultTicketFormatAsync(Uri app, Uri provider, SortedDictionary<string, JsonObject> results)
        {
            var browser = new Browser(app, provider, results, "default ticket format");
            await browser.SendAsync(HttpMethod.Get, "/default-cookie/signin", "sign-in");
            await browser.SendAsync(HttpMethod.Get, "/default-cookie/whoami", "read back");
        }

        public sealed record AuthorizeForm(string Action, List<KeyValuePair<string, string>> Fields);

        // A browser for one scenario: its own cookies for the application, no automatic redirects.
        private sealed class Browser
        {
            private readonly Uri _app;
            private readonly Uri _provider;
            private readonly SortedDictionary<string, JsonObject> _results;
            private readonly string _scenario;
            private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
            private int _step;

            public Browser(Uri app, Uri provider, SortedDictionary<string, JsonObject> results, string scenario)
            {
                _app = app;
                _provider = provider;
                _results = results;
                _scenario = scenario;
            }

            public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);

            public Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathOrUrl, string description, bool record = true, string? bearer = null) =>
                SendAsync(new HttpRequestMessage(method, new Uri(_app, pathOrUrl)), description, record, bearer, null);

            public async Task<AuthorizeForm> AuthorizeAsync(HttpResponseMessage challenge, string? test)
            {
                if (challenge.StatusCode != HttpStatusCode.Redirect || challenge.Headers.Location == null)
                    throw new InvalidOperationException($"{_scenario}: the challenge was {(int)challenge.StatusCode}, not a redirect to the provider");
                string authorize = challenge.Headers.Location.AbsoluteUri + (test == null ? "" : "&wfc_test=" + test);
                using var client = new HttpClient();
                string html = await client.GetStringAsync(authorize);
                var action = Regex.Match(html, "<form method=\"POST\" name=\"hiddenform\" action=\"([^\"]*)\"");
                if (!action.Success) throw new InvalidOperationException($"{_scenario}: the provider's answer has no form_post form: {html}");
                var fields = Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]*)\" value=\"([^\"]*)\" />")
                    .Select(m => new KeyValuePair<string, string>(WebUtility.HtmlDecode(m.Groups[1].Value), WebUtility.HtmlDecode(m.Groups[2].Value))).ToList();
                return new AuthorizeForm(WebUtility.HtmlDecode(action.Groups[1].Value), fields);
            }

            public Task<HttpResponseMessage> PostFormAsync(AuthorizeForm form, string? query, string description)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, form.Action + (query == null ? "" : "?" + query))
                {
                    Content = new FormUrlEncodedContent(form.Fields),
                };
                string body = string.Join("&", form.Fields.Select(f => f.Key + "=" + NormaliseParameter(f.Key, f.Value)));
                return SendAsync(request, description, true, null, body);
            }

            private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string description, bool record, string? bearer, string? normalisedBody)
            {
                if (Cookies.Count > 0)
                    request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", Cookies.Select(c => c.Key + "=" + c.Value)));
                if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                var response = await _client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                    foreach (string setCookie in setCookies) Remember(setCookie);
                if (record)
                {
                    var headers = new JsonObject();
                    foreach (var h in response.Headers.Concat(response.Content.Headers).OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (h.Key == "Date") continue;
                        headers[h.Key] = h.Key switch
                        {
                            "Location" => NormaliseUrl(h.Value.Single()),
                            "Set-Cookie" => string.Join(" | ", h.Value.Select(NormaliseSetCookie).Order(StringComparer.Ordinal)),
                            "Expires" => NormaliseDate(string.Join(", ", h.Value)),
                            _ => Normalise(string.Join(", ", h.Value)),
                        };
                    }
                    string key = $"oidc {_scenario} {++_step} {description}";
                    _results[key] = new JsonObject
                    {
                        ["request"] = new JsonObject
                        {
                            ["method"] = request.Method.Method,
                            ["url"] = request.RequestUri!.AbsoluteUri,
                            ["body"] = normalisedBody,
                            ["headers"] = bearer == null ? null : new JsonObject { ["Authorization"] = "Bearer {" + _scenario.Split(' ').Last() + " token}" },
                        },
                        ["status"] = (int)response.StatusCode,
                        ["headers"] = headers,
                        ["body"] = NormaliseBody(body),
                    };
                }
                return response;
            }

            private void Remember(string setCookie)
            {
                string pair = setCookie.Split(';')[0];
                int equals = pair.IndexOf('=');
                string name = pair[..equals].Trim(), value = pair[(equals + 1)..].Trim();
                var expires = Regex.Match(setCookie, @"expires=([^;]+)", RegexOptions.IgnoreCase);
                bool expired = expires.Success && DateTime.TryParse(expires.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when) && when < DateTime.UtcNow;
                if (expired || value.Length == 0) Cookies.Remove(name);
                else Cookies[name] = value;
            }

            private string Normalise(string text) =>
                text.Replace(_app.GetLeftPart(UriPartial.Authority), "{app}").Replace(_provider.GetLeftPart(UriPartial.Authority), "{provider}");

            // A URL with its query decoded and each parameter's random value replaced.
            private string NormaliseUrl(string url)
            {
                int question = url.IndexOf('?');
                if (question < 0) return Normalise(url);
                var parameters = url[(question + 1)..].Split('&').Select(p =>
                {
                    int equals = p.IndexOf('=');
                    string name = Uri.UnescapeDataString(equals < 0 ? p : p[..equals]);
                    string value = equals < 0 ? "" : Uri.UnescapeDataString(p[(equals + 1)..].Replace('+', ' '));
                    return name + "=" + NormaliseParameter(name, value);
                });
                return Normalise(url[..question]) + "?" + string.Join("&", parameters);
            }

            private string NormaliseParameter(string name, string value)
            {
                switch (name)
                {
                    case "state" when value.StartsWith("OpenIdConnect.AuthenticationProperties=", StringComparison.Ordinal):
                        string format = value["OpenIdConnect.AuthenticationProperties=".Length..];
                        return "OpenIdConnect.AuthenticationProperties=" + (format.StartsWith(SampleStatePrefix, StringComparison.Ordinal) ? SampleStatePrefix : "") + "{protected}";
                    case "nonce":
                        // Katana's nonce: ticks, a dot, and random bytes.
                        return Regex.IsMatch(value, @"^\d+\.[A-Za-z0-9+/=]+$") ? "{ticks}.{random}" : "{" + value.Length + " characters}";
                    case "code":
                    case "id_token":
                    case "id_token_hint":
                        return value.Length == 0 ? "" : "{" + name + "}";
                    // IdentityModel's telemetry: the platform and version of the IdentityModel build, which differ between
                    // webForCore (8.x, ID_NET10_0) and .NET Framework (5.x). OidcGateTests checks webForCore's.
                    case "x-client-SKU":
                        return "{IdentityModel platform}";
                    case "x-client-ver":
                        return "{IdentityModel version}";
                    default:
                        return Normalise(value);
                }
            }

            private const string SampleStatePrefix = "st1.";

            private string NormaliseSetCookie(string setCookie)
            {
                var parts = setCookie.Split(';').Select(p => p.Trim()).ToList();
                int equals = parts[0].IndexOf('=');
                string name = parts[0][..equals], value = parts[0][(equals + 1)..];
                if (name.StartsWith("OpenIdConnect.nonce.", StringComparison.Ordinal)) name = "OpenIdConnect.nonce.{hash}";
                if (value.Length > 0)
                {
                    if (value.StartsWith("chunks:", StringComparison.Ordinal)) { }
                    else if (value.StartsWith("wfc1.", StringComparison.Ordinal)) value = "wfc1.{protected}";
                    else if (name == "OpenIdConnect.nonce.{hash}") value = DecodesTo(value, SampleStatePrefix) ? "base64(" + SampleStatePrefix + "{protected})" : "{protected}";
                    else value = "{protected}";
                }
                parts[0] = name + "=" + value;
                for (int i = 1; i < parts.Count; i++)
                    if (parts[i].StartsWith("expires=", StringComparison.OrdinalIgnoreCase))
                        parts[i] = "expires=" + NormaliseDate(parts[i][8..]);
                return string.Join("; ", parts);
            }

            private static bool DecodesTo(string value, string prefix)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(value))).StartsWith(prefix, StringComparison.Ordinal); }
                catch (FormatException) { return false; }
            }

            // A cookie's or Expires header's date: in the past (deleting a cookie) or the future.
            private static string NormaliseDate(string date) =>
                DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                    ? when < DateTime.UtcNow.AddYears(-10) ? "{long ago}" : when < DateTime.UtcNow ? "{past}" : "{future}"
                    : date;

            private string NormaliseBody(string body)
            {
                body = Normalise(body);
                // Claims.aspx: claims whose values change every sign-in.
                body = Regex.Replace(body, @"^(claim (?:nonce|c_hash|at_hash|iat|nbf|exp|auth_time|uti))=.*$", "$1={random}", RegexOptions.Multiline);
                // ASP.NET's error page ("yellow screen"): the exception's type. Its message is IdentityModel's, whose wording
                // and PII redaction differ between versions, and the page's source and stack sections name the build.
                var ysod = Regex.Match(body, @"<b> Exception Details: </b>([A-Za-z0-9_.]+):");
                if (ysod.Success && body.Contains("Server Error in '/' Application.")) return "ASP.NET error page: " + ysod.Groups[1].Value;
                return body;
            }
        }
    }
}
