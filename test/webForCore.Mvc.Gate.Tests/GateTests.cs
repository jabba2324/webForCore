using System.Net;
using System.Text.Json;
using Xunit;

namespace MvcGate
{
    // The MVC 5 template's routing, controllers, filters and model binding on the fork's System.Web on Kestrel. Each
    // expectation is MVC 5.3's documented result (System.Web.Mvc source). Razor views: ViewTests.
    [Collection(MvcSampleCollection.Name)]
    public class GateTests
    {
        private readonly SampleServer _server;

        public GateTests(SampleServer server)
        {
            _server = server;
        }

        [Theory]
        // JsonResult (JsonRequestBehavior.AllowGet), HttpNotFound
        [InlineData("/Products", 200, "application/json", "[{\"Id\":1,\"Name\":\"Tomato Soup\",\"Price\":1},{\"Id\":2,\"Name\":\"Yo-yo\",\"Price\":3.75}]")]
        [InlineData("/Products/Details/9", 404, null, "")]
        // Binding from the query string: arrays and optional parameters
        [InlineData("/Products/Sum?values=1&values=2&offset=3", 200, "text/plain", "6")]
        [InlineData("/Products/Sum?values[0]=4&values[1]=5", 200, "text/plain", "9")]
        [InlineData("/Products/Sum", 200, "text/plain", "0")]
        // Filters: an action-level filter replaces the controller-level one of the same type (AllowMultiple = false)
        [InlineData("/Filters/Stamped", 200, "text/html", "stamped+action")]
        [InlineData("/Filters/renamed-action", 200, "text/html", "renamed+controller")]
        [InlineData("/Filters/Secret", 401, null, "")]
        // Attribute routes and constraints, areas (AreaRegistration.RegisterAllAreas through BuildManager)
        [InlineData("/greet/World", 200, "text/html", "Hello World")]
        [InlineData("/numbers/12", 200, "text/html", "n=12")]
        [InlineData("/Admin/Dashboard", 200, "text/html", "admin area; link=/")]
        public async Task Get(string path, int status, string mediaType, string body)
        {
            HttpResponseMessage response = await _server.Client.GetAsync(path);

            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(body, await response.Content.ReadAsStringAsync());
            Assert.Equal("5.3", string.Join(",", response.Headers.GetValues("X-AspNetMvc-Version")));
        }

        [Theory]
        // No action or no route: MVC throws HttpException(404), so ASP.NET answers with its error page.
        [InlineData("/Filters/PostOnly")]
        [InlineData("/Home/Missing")]
        [InlineData("/numbers/5")]
        [InlineData("/greet/123")]
        [InlineData("/NoSuchController")]
        public async Task NotFound(string path)
        {
            HttpResponseMessage response = await _server.Client.GetAsync(path);

            Assert.Equal(404, (int)response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task FilterHeaders()
        {
            HttpResponseMessage response = await _server.Client.GetAsync("/Filters/Stamped");

            Assert.Equal("before", string.Join(",", response.Headers.GetValues("X-Stamp-action")));
            Assert.False(response.Headers.Contains("X-Stamp-controller"));
            Assert.Equal("Stamped", string.Join(",", response.Headers.GetValues("X-Controller-OnActionExecuting")));
        }

        [Fact]
        public async Task HttpStatusCodeResultSendsItsDescription()
        {
            HttpResponseMessage response = await _server.Client.GetAsync("/Filters/Status?code=418");

            Assert.Equal(418, (int)response.StatusCode);
            Assert.Equal("Custom description", response.ReasonPhrase);
        }

        [Fact]
        public async Task UnhandledExceptionIsTheAspNetErrorPage()
        {
            // HandleErrorAttribute is registered (FilterConfig) but customErrors is RemoteOnly and this request is local.
            HttpResponseMessage response = await _server.Client.GetAsync("/Filters/Throws");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(500, (int)response.StatusCode);
            Assert.Contains("<title>Boom", body);
            Assert.Contains("[InvalidOperationException]: Boom", body);
        }

        [Fact]
        public async Task ComplexModelBinding()
        {
            HttpResponseMessage response = await _server.Client.PostAsync("/Products/Echo", Form(
                ("Name", "W"), ("Price", "2.5"), ("Tags[0]", "a"), ("Tags[1]", "b"), ("Supplier.City", "Oslo"), ("Supplier.Country", "NO")));

            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal("{\"Name\":\"W\",\"Price\":2.5,\"Tags\":[\"a\",\"b\"],\"City\":\"Oslo\",\"Country\":\"NO\"}",
                await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ValidationErrors()
        {
            HttpResponseMessage response = await _server.Client.PostAsync("/Products/Create", Form(("Price", "5000")));

            Assert.Equal(400, (int)response.StatusCode);
            // ModelState keys in insertion order: Price when bound, Name when validated.
            Assert.Equal("{\"Price\":[\"The field Price must be between 0 and 1000.\"],\"Name\":[\"The Name field is required.\"]}",
                await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task PostRedirectGetWithTempData()
        {
            using var client = CookieClient();

            HttpResponseMessage post = await client.PostAsync("/Products/Create", Form(("Name", "Widget"), ("Price", "5")));
            Assert.Equal(302, (int)post.StatusCode);
            Assert.Equal("/Products/Details/2", post.Headers.Location.OriginalString);

            // TempData (session state) survives the redirect once, then is gone.
            Assert.Contains("<p id=\"saved\">Saved Widget</p>", await client.GetStringAsync("/Products/Details/2"));
            Assert.DoesNotContain("id=\"saved\"", await client.GetStringAsync("/Products/Details/2"));
        }

        [Fact]
        public async Task AntiForgeryTokens()
        {
            using var client = CookieClient();

            // Missing tokens: HttpAntiForgeryException, a 500.
            HttpResponseMessage rejected = await client.PostAsync("/Filters/Protected", Form(("x", "1")));
            Assert.Equal(500, (int)rejected.StatusCode);
            Assert.Contains("HttpAntiForgeryException", await rejected.Content.ReadAsStringAsync());

            // Tokens from AntiForgery.GetTokens (machine key protection), sent back as cookie and form field.
            using JsonDocument tokens = JsonDocument.Parse(await client.GetStringAsync("/Filters/Tokens"));
            string cookieToken = tokens.RootElement.GetProperty("cookieToken").GetString();
            string formToken = tokens.RootElement.GetProperty("formToken").GetString();
            var request = new HttpRequestMessage(HttpMethod.Post, "/Filters/Protected")
            {
                Content = Form(("__RequestVerificationToken", formToken)),
            };
            request.Headers.Add("Cookie", "__RequestVerificationToken=" + cookieToken);
            HttpResponseMessage accepted = await client.SendAsync(request);
            Assert.Equal(200, (int)accepted.StatusCode);
            Assert.Equal("validated+controller", await accepted.Content.ReadAsStringAsync());

            // A form token from another cookie token does not validate.
            using JsonDocument other = JsonDocument.Parse(await client.GetStringAsync("/Filters/Tokens"));
            var mismatched = new HttpRequestMessage(HttpMethod.Post, "/Filters/Protected")
            {
                Content = Form(("__RequestVerificationToken", other.RootElement.GetProperty("formToken").GetString())),
            };
            mismatched.Headers.Add("Cookie", "__RequestVerificationToken=" + cookieToken);
            Assert.Equal(500, (int)(await client.SendAsync(mismatched)).StatusCode);
        }

        private HttpClient CookieClient() =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = _server.BaseAddress };

        private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) =>
            new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
    }
}
