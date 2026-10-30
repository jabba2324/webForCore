using Xunit;

namespace MvcGate
{
    // Razor views compiled at run time by the fork's BuildManager (System.Web.WebPages.Razor's RazorBuildProvider,
    // MvcWebRazorHostFactory from Views/web.config): the template's views with _ViewStart and _Layout, sections,
    // partials, child actions, inline and App_Code helpers, display and editor templates, HTML helpers and encoding.
    [Collection(MvcSampleCollection.Name)]
    public class ViewTests
    {
        private readonly SampleServer _server;

        public ViewTests(SampleServer server)
        {
            _server = server;
        }

        private async Task<string> Html(string path)
        {
            HttpResponseMessage response = await _server.Client.GetAsync(path);
            Assert.Equal(200, (int)response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            return (await response.Content.ReadAsStringAsync()).Replace("\r\n", "\n");
        }

        [Theory]
        [InlineData("/", "<title>Home Page - My ASP.NET Application</title>", "<h1>ASP.NET</h1>")]
        [InlineData("/Home", "<title>Home Page - My ASP.NET Application</title>", "<h1>ASP.NET</h1>")]
        [InlineData("/home/index/", "<title>Home Page - My ASP.NET Application</title>", "<h1>ASP.NET</h1>")]
        [InlineData("/Home/About", "<title>About - My ASP.NET Application</title>", "<h3>Your application description page.</h3>")]
        [InlineData("/Home/Contact", "<title>Contact - My ASP.NET Application</title>", "<abbr title=\"Phone\">P:</abbr>")]
        public async Task TemplateViewsUseTheLayout(string path, string title, string body)
        {
            string html = await Html(path);

            Assert.StartsWith("<!DOCTYPE html>", html);
            Assert.Contains(title, html);
            Assert.Contains(body, html);
            // _Layout: child action (Html.Action) and footer. Routing reuses the request's "home" when the link's "Home"
            // matches it apart from case.
            Assert.Contains("/About\">About</a></li>", html);
            Assert.Contains("<p>&copy; " + DateTime.Now.Year + " - My ASP.NET Application</p>", html);
        }

        [Theory]
        // Views, source and configuration are not served. IIS (integrated mode) answers 404 for /Views through
        // Views/web.config's BlockViewHandler (system.webServer, which the fork does not read); here the classic
        // mappings answer 403 (HttpForbiddenHandler for *.cshtml, *.config, *.cs, *.asax).
        [InlineData("/Views/Home/Index.cshtml", 403)]
        [InlineData("/Views/web.config", 403)]
        [InlineData("/Web.config", 403)]
        [InlineData("/Global.asax", 403)]
        [InlineData("/Controllers/HomeController.cs", 403)]
        public async Task SourceIsNotServed(string path, int status)
        {
            Assert.Equal(status, (int)(await _server.Client.GetAsync(path)).StatusCode);
        }

        [Fact]
        public async Task RazorFeatures()
        {
            string html = await Html("/Home/Features");

            Assert.Contains("<span class=\"badge\">items (3)</span>", html);                // inline @helper
            Assert.Contains("<span class=\"money\">1,234.50</span>", html);                 // App_Code helper
            Assert.Contains("<p id=\"encoded\">&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;</p>", html);
            Assert.Contains("<p id=\"raw\"><b>bold</b></p>", html);
            Assert.Contains("<em>Hello, partial world!</em>", html);                        // Html.Partial with a model
            Assert.Contains("<p id=\"url\">/Products/Details/2</p>", html);
            Assert.Contains("<p id=\"area-link\"><a href=\"/Admin/Dashboard\">Admin</a></p>", html);
            // The section is rendered by the layout, after the body.
            Assert.True(html.IndexOf("<script id=\"section-script\">", StringComparison.Ordinal) > html.IndexOf("<footer>", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TypedViewWithDisplayTemplates()
        {
            string html = await Html("/Products/Details/2");

            Assert.Contains("<title>Yo-yo - My ASP.NET Application</title>", html);
            Assert.Contains("<h2>Yo-yo</h2>", html);
            Assert.Contains("<dt>Price</dt>", html);
            Assert.Contains("<dd id=\"price\">3.75</dd>", html);
            Assert.Contains("<span class=\"address\">Oslo, NO</span>", html);               // DisplayTemplates/Address
            Assert.Contains("<li>toy</li>", html);
            Assert.Contains("<li>classic</li>", html);
        }

        [Fact]
        public async Task FormWithEditorTemplatesValidationAndAntiForgery()
        {
            string html = await Html("/Products/Edit/2");

            Assert.Contains("<form action=\"/Products/Create\" id=\"edit-form\" method=\"post\">", html);
            Assert.Matches("<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"[^\"]+\" />", html);
            Assert.Contains("<label for=\"Name\">Name</label>", html);
            Assert.Contains("<input class=\"text-box single-line\" data-val=\"true\" data-val-length=\"The field Name must be a string with a maximum length of 20.\" " +
                            "data-val-length-max=\"20\" data-val-required=\"The Name field is required.\" id=\"Name\" name=\"Name\" type=\"text\" value=\"Yo-yo\" />", html);
            Assert.Contains("data-val-range=\"The field Price must be between 0 and 1000.\"", html);
            Assert.Contains("<span class=\"field-validation-valid\" data-valmsg-for=\"Price\" data-valmsg-replace=\"true\"></span>", html);
            Assert.Contains("<input id=\"Supplier_City\" name=\"Supplier.City\" type=\"text\" value=\"Oslo\" />", html); // EditorTemplates/Address
        }

        [Fact]
        public async Task FormRoundTripWithAntiForgeryToken()
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new System.Net.CookieContainer() })
            {
                BaseAddress = _server.BaseAddress,
            };
            string html = await client.GetStringAsync("/Products/Edit/2");
            string token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

            // Products/Create has no [ValidateAntiForgeryToken]; Filters/Protected validates the form's token and cookie.
            HttpResponseMessage response = await client.PostAsync("/Filters/Protected",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
            Assert.Equal(200, (int)response.StatusCode);
        }
    }
}
