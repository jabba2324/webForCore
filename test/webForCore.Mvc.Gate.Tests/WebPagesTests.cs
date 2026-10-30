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
        // As on IIS: .NET Framework's root mappings (HttpForbiddenHandler: 403) and Web Pages' own rules.
        [InlineData("/Default.cshtml", 200)]
        [InlineData("/_PageStart.cshtml", 404)]
        [InlineData("/Shared/_Layout.cshtml", 404)]
        [InlineData("/Web.config", 403)]
        [InlineData("/Program.cs", 403)]
        [InlineData("/webForCore.WebPages.Sample.csproj", 403)]
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
    }
}
