using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace IISGate
{
    // <system.webServer> in test/webForCore.IIS.Sample/Web.config, against IIS 10's documented behaviour.
    [Collection(IISSampleCollection.Name)]
    public class IISModuleTests
    {
        private readonly SampleServer _server;

        public IISModuleTests(SampleServer server)
        {
            _server = server;
        }

        private HttpClient Client => _server.Client;

        private async Task<(HttpResponseMessage Response, string Body)> Get(string url, string acceptEncoding = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (acceptEncoding != null)
            {
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(acceptEncoding));
            }
            var response = await Client.SendAsync(request);
            return (response, await response.Content.ReadAsStringAsync());
        }

        // Request filtering: IIS answers 404 with a substatus; httpErrors maps 404.7 to its own page, other 404s to 404.htm.
        [Theory]
        [InlineData("/secret.secret", "Custom 404.7 page")]          // <fileExtensions> deny (404.7)
        [InlineData("/Default.aspx.cs", "Custom 404.7 page")]        // applicationHost.config's .cs deny (404.7)
        [InlineData("/Global.asax", "Custom 404.7 page")]
        [InlineData("/private/data.txt", "Custom 404 page")]         // <hiddenSegments> (404.8)
        [InlineData("/App_Data/logs/x.txt", "Custom 404 page")]      // applicationHost.config's hidden segments
        [InlineData("/web.config", "Custom 404 page")]
        [InlineData("/bin_dotnet/System.Web.dll", "Custom 404 page")] // the app's output folder
        [InlineData("/x/forbidden-sequence/y", "Custom 404 page")]   // <denyUrlSequences> (404.5)
        [InlineData("/a%252fb.htm", "Custom 404 page")]              // double escaping (404.11)
        [InlineData("/a+b.htm", "Custom 404 page")]                  // '+' with allowDoubleEscaping="false" (404.11)
        public async Task RequestFilteringDenies(string url, string page)
        {
            var (response, body) = await Get(url);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(page, body);
        }

        [Fact]
        public async Task RequestFilteringDeniesLongQueryString()
        {
            var (response, body) = await Get("/Default.aspx?q=" + new string('x', 120)); // maxQueryString="100" (404.15)
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Custom 404 page", body);
        }

        [Fact]
        public async Task RequestFilteringDeniesVerb()
        {
            var response = await Client.SendAsync(new HttpRequestMessage(new HttpMethod("TRACE"), "/Default.aspx")); // 404.6
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task CustomHeaders()
        {
            var (response, _) = await Get("/Default.aspx");
            Assert.Equal("ASP.NET", Assert.Single(response.Headers.GetValues("X-Powered-By"))); // applicationHost.config's
            Assert.Equal("iis", Assert.Single(response.Headers.GetValues("X-Gate-Header")));
            Assert.False(response.Headers.Contains("X-Gate-Redirect")); // <redirectHeaders>: 3xx only
        }

        [Fact]
        public async Task CustomHeadersOnErrorsAndRedirectHeaders()
        {
            var (error, _) = await Get("/secret.secret");
            Assert.Equal("iis", Assert.Single(error.Headers.GetValues("X-Gate-Header")));
            var (redirect, _) = await Get("/legacy");
            Assert.Equal("yes", Assert.Single(redirect.Headers.GetValues("X-Gate-Redirect")));
        }

        [Fact]
        public async Task HttpRedirectAppendsTheRestOfTheUrl()
        {
            var (response, _) = await Get("/old/page.htm?x=1"); // <location path="old"><httpRedirect destination="/new" Permanent>
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal("/new/page.htm?x=1", response.Headers.Location.OriginalString);
        }

        [Fact]
        public async Task UrlRewriteRewrites()
        {
            var (response, body) = await Get("/products/42");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Product 42 (/Product.aspx)", body);
        }

        [Fact]
        public async Task UrlRewriteRedirects()
        {
            var (response, _) = await Get("/legacy");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.EndsWith("/Default.aspx", response.Headers.Location.OriginalString);
        }

        [Fact]
        public async Task DefaultDocument()
        {
            var (root, rootBody) = await Get("/");
            Assert.Equal(HttpStatusCode.OK, root.StatusCode);
            Assert.Contains("IIS gate home", rootBody); // applicationHost.config's list: default.aspx
            var (docs, docsBody) = await Get("/docs/");
            Assert.Contains("docs home", docsBody); // <location path="docs"><defaultDocument><clear/><add value="home.aspx"/>
        }

        [Fact]
        public async Task DirectoryCourtesyRedirect()
        {
            var (response, _) = await Get("/docs?a=b");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal("/docs/?a=b", response.Headers.Location.OriginalString);
        }

        [Fact]
        public async Task StaticContentMimeMapAndClientCache()
        {
            var (gate, gateBody) = await Get("/file.gate");
            Assert.Equal("text/x-gate", gate.Content.Headers.ContentType.MediaType); // <mimeMap>
            Assert.Contains("gate file", gateBody);
            var (css, _) = await Get("/site.css");
            Assert.Equal("text/css", css.Content.Headers.ContentType.MediaType);
            Assert.Equal(TimeSpan.FromDays(1), css.Headers.CacheControl.MaxAge); // <clientCache UseMaxAge 1.00:00:00>
            var (dynamicPage, _) = await Get("/Default.aspx");
            Assert.Null(dynamicPage.Headers.CacheControl?.MaxAge);
        }

        [Fact]
        public async Task StaticContentWithoutMimeMapIs404()
        {
            var (response, body) = await Get("/file.unknownext"); // 404.3
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Custom 404 page", body);
        }

        // existingResponse="Auto": ASP.NET sets TrySkipIisCustomErrors on its error pages, so they pass through.
        [Fact]
        public async Task HttpErrorsAutoPassesAspNetErrorsThrough()
        {
            var (response, body) = await Get("/missing.aspx");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("The resource cannot be found.", body);
        }

        // existingResponse="Replace" with <error statusCode="500" responseMode="ExecuteURL" path="/Error.aspx"/>.
        [Fact]
        public async Task HttpErrorsReplaceExecutesUrl()
        {
            var (response, body) = await Get("/replace/Throw.aspx");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Custom 500 page", body);
            Assert.DoesNotContain("InvalidOperationException", body);
        }

        [Fact]
        public async Task ManagedModuleFromSystemWebServer()
        {
            var (response, _) = await Get("/Default.aspx");
            Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Gate-Module")));
        }

        [Fact]
        public async Task ModuleRemovedInSystemWebServer()
        {
            var (_, body) = await Get("/Session.aspx"); // <modules><remove name="Session"/>
            Assert.Contains("session=none", body);
        }

        [Fact]
        public async Task ManagedHandlerFromSystemWebServer()
        {
            var (response, body) = await Get("/gate.axd");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("gate handler", body);
        }

        [Fact]
        public async Task UrlAuthorizationDeniesAnonymous()
        {
            var (response, _) = await Get("/members/"); // <location path="members"> <add accessType="Deny" users="?"/>
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // <urlCompression doStaticCompression="true" doDynamicCompression="false"/> (applicationHost.config's defaults).
        [Fact]
        public async Task StaticCompressionOnly()
        {
            var (css, _) = await Get("/site.css", "gzip");
            Assert.Equal("gzip", Assert.Single(css.Content.Headers.ContentEncoding));
            var (page, _) = await Get("/Default.aspx", "gzip");
            Assert.Empty(page.Content.Headers.ContentEncoding);
            var (small, _) = await Get("/file.gate", "gzip"); // under minFileSizeForComp (2700 bytes)
            Assert.Empty(small.Content.Headers.ContentEncoding);
        }

        [Fact]
        public async Task W3CLog()
        {
            await Get("/Default.aspx?w3c=1");
            var logs = Path.Combine(_server.SampleDirectory, "App_Data", "logs");
            string text = "";
            // ASP.NET Core's W3C logger writes cs-uri-query with its '?' (IIS without) and its own field order.
            for (int i = 0; i < 50 && !text.Contains("GET /Default.aspx ?w3c=1 200"); i++)
            {
                await Task.Delay(200);
                text = Directory.Exists(logs)
                    ? string.Join("\n", Directory.GetFiles(logs, "u_ex*").Select(f => { using var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return new StreamReader(s).ReadToEnd(); }))
                    : "";
            }
            Assert.Contains("#Fields: date time c-ip cs-username s-ip s-port cs-method cs-uri-stem cs-uri-query sc-status time-taken cs(User-Agent) cs(Referer)", text);
            Assert.Contains("GET /Default.aspx ?w3c=1 200", text);
        }
    }
}
