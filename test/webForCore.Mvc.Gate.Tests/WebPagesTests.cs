using Xunit;

namespace MvcGate
{
    // An ASP.NET Web Pages site (test/webForCore.WebPages.Sample): .cshtml pages without MVC, routed by Web Pages'
    // extensionless routing and compiled at run time by the fork's BuildManager.
    public class WebPagesTests : IClassFixture<WebPagesServer>
    {
        private readonly WebPagesServer _server;

        public WebPagesTests(WebPagesServer server)
        {
            _server = server;
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/Default")]
        [InlineData("/Default.cshtml")]
        public async Task DefaultPage(string path)
        {
            string html = (await _server.Client.GetStringAsync(path + "?name=Kestrel")).Replace("\r\n", "\n");

            Assert.Contains("<title>Home - Web Pages Sample</title>", html);                         // _AppStart (AppState), layout
            Assert.Contains("<header>header data / page start: yes</header>", html);                  // RenderPage with data, _PageStart
            Assert.Contains("<h1>Hello, Kestrel!</h1>", html);                                         // Request
            Assert.Contains("<strong>KESTREL</strong>", html);                                         // @helper
            Assert.Contains("<input id=\"name\" name=\"name\" type=\"text\" value=\"Kestrel\" />", html); // Html.TextBox
            Assert.Contains("<p id=\"href\">/Products/a%20b</p>", html);                               // Href
            Assert.Contains("<footer>Footer section</footer>", html);                                  // section
        }

        [Fact]
        public async Task UrlData()
        {
            Assert.Contains("<li>Hammer</li>  <li>Tomato Soup</li>  <li>Yo-yo</li>", await _server.Client.GetStringAsync("/Products"));
            Assert.Contains("<p id=\"product\">Product 2: Yo-yo</p>", await _server.Client.GetStringAsync("/Products/2"));
        }

        [Theory]
        // As on IIS: request filtering (web.config 404.8, .cs/.csproj 404.7) and Web Pages' own rules.
        [InlineData("/Default.cshtml", 200)]
        [InlineData("/_PageStart.cshtml", 404)]
        [InlineData("/Shared/_Layout.cshtml", 404)]
        [InlineData("/Web.config", 404)]
        [InlineData("/Program.cs", 404)]
        [InlineData("/webForCore.WebPages.Sample.csproj", 404)]
        // ASP.NET Core's settings files are blocked by the fork's middleware.
        [InlineData("/appsettings.json", 404)]
        [InlineData("/appsettings.Production.json", 404)]
        public async Task FileAccess(string path, int status)
        {
            Assert.Equal(status, (int)(await _server.Client.GetAsync(path)).StatusCode);
        }

        [Fact]
        public async Task MissingPageIsNotFound()
        {
            HttpResponseMessage response = await _server.Client.GetAsync("/Nope");

            Assert.Equal(404, (int)response.StatusCode);
        }

        [Fact]
        public async Task OutOfProcessSessionState()
        {
            // The sample's session state is UseAspNetCoreSessionProvider()'s, in place of Web.config's StateServer: the
            // session is found again by <sessionState cookieName>, the only cookie, as on IIS.
            var get = new HttpRequestMessage(HttpMethod.Get, "/Visits");
            HttpResponseMessage first = await _server.Client.SendAsync(get);
            Assert.Equal("visits=1", (await first.Content.ReadAsStringAsync()).Trim());
            string cookie = Assert.Single(first.Headers.GetValues("Set-Cookie"));
            Assert.StartsWith("WebPagesSample_SessionId=", cookie);

            var again = new HttpRequestMessage(HttpMethod.Get, "/Visits");
            again.Headers.Add("Cookie", cookie.Substring(0, cookie.IndexOf(';')));
            Assert.Equal("visits=2", (await (await _server.Client.SendAsync(again)).Content.ReadAsStringAsync()).Trim());
        }

        [Fact]
        public async Task OutOfProcessSessionStateNeedsSerializableValues()
        {
            // #180: out of process, as with StateServer and SQLServer on IIS, every session value is serialized at the end
            // of the request, and one that isn't [Serializable] fails the request with ASP.NET's error. In-process session
            // state (the default) keeps the object itself (GateTests.InProcSessionStateKeepsTheObjectsThemselves).
            HttpResponseMessage response = await _server.Client.GetAsync("/Visits?keep=unserializable");

            Assert.Equal(500, (int)response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Unable to serialize the session state", body);
            Assert.Contains("is not marked as serializable", body);
        }
    }
}
