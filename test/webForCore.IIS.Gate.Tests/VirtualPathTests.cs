using System.Net;
using System.Text.Json;
using Xunit;

namespace IISGate
{
    // An application under a virtual path, as IIS runs the application /app of a site (#190): test/webForCore.VirtualPath.Sample
    // with UseWebForms(o => o.VirtualPath("/app")), Web Forms pages, MVC and Web API routes, bundles, forms authentication
    // and session state. Every response is written to vpath-responses.json beside the test assembly, and CI's iis-compare
    // job sends the same requests to the same application on IIS 10 (test/webForCore.VirtualPath.Sample.Iis, the
    // application /app of a site whose root is empty) and diffs the two (tools/ci/iis_compare.cs).
    [Collection(VirtualPathSampleCollection.Name)]
    public class VirtualPathTests
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, object> Recorded = new Dictionary<string, object>();
        private readonly VirtualPathSampleServer _server;

        public VirtualPathTests(VirtualPathSampleServer server)
        {
            _server = server;
        }

        // HttpRuntime.AppDomainAppVirtualPath, Request.ApplicationPath, the request's paths and URL, and everything System.Web
        // makes from "~/" (ResolveUrl, VirtualPathUtility, FormsAuthentication.LoginUrl, bundle URLs) have the virtual path.
        [Fact]
        public async Task SystemWebSeesTheVirtualPath()
        {
            var (response, body) = await Get("/app/Probe.aspx?q=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("/app", values["app_domain_app_virtual_path"]);
            Assert.Equal("/app", values["application_path"]);
            Assert.Equal("/app/Probe.aspx", values["path"]);
            Assert.Equal("/app/Probe.aspx", values["file_path"]);
            Assert.Equal("/app/Probe.aspx", values["current_execution_file_path"]);
            Assert.Equal("~/Probe.aspx", values["app_relative"]);
            Assert.Equal("/app/Probe.aspx?q=1", values["raw_url"]);
            Assert.Equal("/app/Probe.aspx", values["url_path"]);
            Assert.Equal("/app/Scripts/site.js", values["resolve_url"]);
            Assert.Equal("Scripts/site.js", values["resolve_client_url"]);
            Assert.Equal("/app/Account/Login.aspx", values["to_absolute"]);
            Assert.Equal("/app/", values["to_absolute_root"]);
            Assert.Equal("~/Probe.aspx", values["to_app_relative"]);
            Assert.Equal("/app/Account/Login.aspx", values["login_url"]);
            Assert.Equal("/app/Probe.aspx", values["default_url"]);
            Assert.Equal("/", values["forms_cookie_path"]); // <forms path> is "/" by default, under a virtual path too
            Assert.StartsWith("/app/bundles/site?v=", values["bundle_script"]);
            Assert.StartsWith("/app/Content/css?v=", values["bundle_style"]);
            Assert.Equal("/LM/W3SVC/n/ROOT/app", values["appl_md_path"]);
            Assert.Equal("/app/Probe.aspx", values["script_name"]);
            Assert.Equal("/app/Probe.aspx", values["url_var"]);
        }

        // IIS matches an application's path in any case; the request's own paths keep the case it was asked in.
        [Fact]
        public async Task TheApplicationInAnotherCase()
        {
            var (response, body) = await Get("/APP/Probe.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("/app", values["application_path"]);
            Assert.Equal("/APP/Probe.aspx", values["path"]);
            Assert.Equal("~/Probe.aspx", values["app_relative"]);
            Assert.Equal("/app/Account/Login.aspx", values["login_url"]);
        }

        // A URL outside the application isn't the application's: 404, as IIS answers for a site whose root has nothing
        // there. It was the application's, and failed with 500 ("Failed to map the path").
        [Theory]
        [InlineData("/Probe.aspx")]
        [InlineData("/other")]
        [InlineData("/appx/Probe.aspx")]
        public async Task UrlsOutsideTheApplicationAreNotFound(string path)
        {
            var (response, _) = await Get(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // The application's own URL without its trailing slash: MVC's default route takes it ("~/"), as UrlRoutingModule
        // does on IIS, so there is no courtesy redirect; a directory of the application without its slash is redirected,
        // to the absolute URL, as IIS does.
        [Fact]
        public async Task TheApplicationWithoutItsTrailingSlash()
        {
            var (response, body) = await Get("/app");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("Home", values["controller"]);
            Assert.Equal("/app", values["path"]);
            Assert.Equal("~/", values["app_relative"]);
            Assert.Equal("/app/", values["content"]);

            (response, _) = await Get("/app/Content");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal(_server.BaseAddress.GetLeftPart(UriPartial.Authority) + "/app/Content/", response.Headers.Location.OriginalString);
        }

        // MVC's URLs: Url.Content, Url.Action, Url.RouteUrl (relative and absolute), and Web API's route from MVC.
        [Fact]
        public async Task MvcUrls()
        {
            var (response, body) = await Get("/app/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("/app/", values["content"]);
            Assert.Equal("/app/Scripts/site.js", values["content_script"]);
            Assert.Equal("/app/Home/About", values["action"]);
            Assert.Equal("/app/", values["action_index"]);
            Assert.Equal("/app/Home/About/7", values["route"]);
            Assert.Equal(_server.BaseAddress.GetLeftPart(UriPartial.Authority) + "/app/Home/About", values["route_http"]);
            Assert.Equal("/app/api/links/3", values["api"]);
            Assert.Equal("/app", values["application_path"]);

            (response, body) = await Get("/app/Home/About");
            Assert.Equal("about /app/Home/About", body);
            (response, _) = await Get("/app/Home/Back");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/app/Home/About", response.Headers.Location.OriginalString);
        }

        // Web API's URLs: Url.Link (absolute), Url.Route, RequestContext.VirtualPathRoot, and an attribute route.
        [Fact]
        public async Task WebApiUrls()
        {
            var (response, body) = await Get("/app/api/links/1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(body);
            string site = _server.BaseAddress.GetLeftPart(UriPartial.Authority);
            Assert.Equal(site + "/app/api/links/5", values["link"]);
            Assert.Equal("/app/api/links/6", values["route"]);
            Assert.Equal(site + "/app/Scripts/site.js", values["content"]);
            Assert.Equal("/app", values["virtual_path_root"]);
            Assert.Equal("/app/api/links/1", values["path"]);

            (response, body) = await Get("/app/api/attribute/x");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"\"{site}/app/api/links x\"", body);
        }

        // Swashbuckle 5's Swagger document: its root URL is the request's host and Web API's virtual path root
        // (basePath). Swashbuckle takes the scheme, host and port from X-Forwarded-Proto, -Host and -Port itself, whatever
        // the host's settings (#194), as on IIS; the request compared with IIS sends them, so that the host is the same.
        [Fact]
        public async Task SwashbuckleRootUrl()
        {
            var (response, body) = await _server.Get("/app/swagger/docs/v1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(body);
            Assert.Equal($"127.0.0.1:{_server.BaseAddress.Port}", document.RootElement.GetProperty("host").GetString());
            Assert.Equal("/app", document.RootElement.GetProperty("basePath").GetString());
            Assert.Equal("http", document.RootElement.GetProperty("schemes")[0].GetString());
            Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/api/Links/{id}", out _), body);

            (response, body) = await Get("/app/swagger/docs/v1", new Dictionary<string, string>
            {
                ["X-Forwarded-Host"] = "www.example.com",
                ["X-Forwarded-Port"] = "443",
                ["X-Forwarded-Proto"] = "https",
            });
            using var forwarded = JsonDocument.Parse(body);
            Assert.Equal("www.example.com", forwarded.RootElement.GetProperty("host").GetString()); // the scheme's default port left out
            Assert.Equal("/app", forwarded.RootElement.GetProperty("basePath").GetString());
            Assert.Equal("https", forwarded.RootElement.GetProperty("schemes")[0].GetString());
        }

        // System.Web.Optimization's tags and the bundles themselves under the virtual path.
        [Fact]
        public async Task Bundles()
        {
            var (response, body) = await Get("/app/Bundles.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Matches("<script src=\"/app/bundles/site\\?v=[^\"]+\"></script>", body);
            Assert.Matches("<link href=\"/app/Content/css\\?v=[^\"]+\" rel=\"stylesheet\"/>", body);

            (response, body) = await Get("/app/bundles/site");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("function greet(n){return\"Hello, \"+n}", body);
            (response, body) = await Get("/app/Content/css");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("url(\"images/back.png\")", body);
        }

        // Response.Redirect("~/...") goes to the application's URL.
        [Fact]
        public async Task RedirectToAnApplicationRelativeUrl()
        {
            var (response, _) = await Get("/app/Redirect.aspx");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/app/Probe.aspx?from=redirect", response.Headers.Location.OriginalString);
        }

        // Forms authentication: the redirect to LoginUrl with the ReturnUrl, the forms and session cookies (path "/", as on
        // IIS: <forms path> and the session cookie don't take the application's path), and a signed-in request. The sample
        // has <forms requireSSL="true">: the module makes the request secure (ServerVariables["HTTPS"], #194).
        [Fact]
        public async Task FormsAuthenticationAndSession()
        {
            var (response, _) = await Get("/app/Secure/Page.aspx");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/app/Account/Login.aspx?ReturnUrl=%2fapp%2fSecure%2fPage.aspx", response.Headers.Location.OriginalString);

            (response, var body) = await Get("/app/Account/Login.aspx?user=gate");
            Assert.Contains("error=The application is configured to issue secure cookies.", body);
            string session = Assert.Single(response.Headers.GetValues("Set-Cookie"));
            Assert.Matches("^ASP.NET_SessionId=[a-z0-5]{24}; path=/; HttpOnly; SameSite=Lax$", session);

            var https = new Dictionary<string, string> { ["X-Module-Https"] = "on" };
            (response, body) = await Get("/app/Account/Login.aspx?user=gate", https);
            Assert.Contains("cookie=.ASPXAUTH;path=/;secure=True;httponly=True", body);
            string forms = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".ASPXAUTH=", StringComparison.Ordinal));
            Assert.EndsWith("; path=/; secure; HttpOnly; SameSite=Lax", forms);

            var signedIn = new Dictionary<string, string>(https) { ["Cookie"] = forms.Split(';')[0] };
            (response, body) = await _server.Get("/app/Secure/Page.aspx", signedIn);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("user=gate\n", body);
        }

        // A module that sets REMOTE_ADDR and HTTPS (Request.ServerVariables) changes what the application sees of the
        // connection, as in IIS's integrated pipeline (#194): Request.UserHostAddress, IsSecureConnection, Url's scheme.
        // Before, only the ServerVariables collection had the new values. REMOTE_HOST is IIS's own variable.
        [Fact]
        public async Task AModuleSetsRemoteAddrAndHttps()
        {
            var (response, body) = await Get("/app/Probe.aspx", new Dictionary<string, string>
            {
                ["X-Module-Remote-Addr"] = "203.0.113.5",
                ["X-Module-Https"] = "on",
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("203.0.113.5", values["user_host_address"]);
            Assert.Equal("203.0.113.5", values["remote_addr"]);
            Assert.Equal("127.0.0.1", values["remote_host"]);
            Assert.Equal("True", values["is_secure_connection"]);
            Assert.Equal("on", values["https"]);
            Assert.Equal("https", values["url_scheme"]);

            // MVC's [RequireHttps] sees the request as secure.
            (response, body) = await Get("/app/Home/Secure", new Dictionary<string, string> { ["X-Module-Https"] = "on" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("secure https", body);
        }

        // Without any setting, X-Forwarded-For and X-Forwarded-Proto are request headers like any other, as on IIS: the
        // client's address and the scheme are the connection's (#194).
        [Fact]
        public async Task ForwardedHeadersAreOnlyHeadersByDefault()
        {
            var (response, body) = await Get("/app/Probe.aspx", new Dictionary<string, string>
            {
                ["X-Forwarded-For"] = "203.0.113.7",
                ["X-Forwarded-Proto"] = "https",
            });
            var values = VirtualPathSampleServer.Values(body);
            Assert.Equal("127.0.0.1", values["user_host_address"]);
            Assert.Equal("False", values["is_secure_connection"]);
            Assert.Equal("off", values["https"]);
            Assert.Equal("http", values["url_scheme"]);
            Assert.Equal("203.0.113.7", values["x_forwarded_for"]);

            (response, _) = await Get("/app/Home/Secure");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("https://127.0.0.1/app/Home/Secure", response.Headers.Location.OriginalString);
            (response, _) = await Get("/app/Restricted/Page.aspx", new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.7" }, record: false);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        private async Task<(HttpResponseMessage Response, string Body)> Get(string path, IDictionary<string, string> requestHeaders = null, bool record = true)
        {
            var (response, body) = await _server.Get(path, requestHeaders);
            if (record) Record("GET " + path + (requestHeaders != null ? " (" + string.Join(", ", requestHeaders.Keys.Order()) + ")" : ""), response, body, requestHeaders);
            return (response, body);
        }

        // As RequestHandlingTests records its requests for the comparison with IIS.
        private static void Record(string key, HttpResponseMessage response, string body, IDictionary<string, string> requestHeaders)
        {
            HttpRequestMessage sent = response.RequestMessage;
            var request = new
            {
                method = sent.Method.Method,
                url = sent.RequestUri.AbsoluteUri,
                accept = (string)null,
                contentType = (string)null,
                body = (string)null,
                headers = requestHeaders == null ? null : new SortedDictionary<string, string>(requestHeaders, StringComparer.OrdinalIgnoreCase),
            };
            var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers.Concat(response.Content.Headers))
            {
                if (h.Key != "Date") headers[h.Key] = string.Join(", ", h.Value);
            }
            lock (Gate)
            {
                Recorded[key] = new { request, status = (int)response.StatusCode, headers, body };
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "vpath-responses.json"),
                    JsonSerializer.Serialize(new SortedDictionary<string, object>(Recorded, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}
