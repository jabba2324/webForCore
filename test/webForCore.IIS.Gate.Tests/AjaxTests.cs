using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace IISGate
{
    // ASP.NET AJAX (System.Web.Extensions) on ajax/Panel.aspx: the ScriptManager's scripts from ScriptResource.axd, an
    // UpdatePanel's partial postback, and a page method called with JSON.
    [Collection(IISSampleCollection.Name)]
    public class AjaxTests
    {
        private const string Url = "/ajax/Panel.aspx";

        // ScriptManager.SupportsPartialRendering comes from the browser definitions: a client they don't recognise (no
        // User-Agent, curl) gets no partial rendering, as on .NET Framework, so these requests come from a browser.
        private const string Browser = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        private readonly SampleServer _server;

        public AjaxTests(SampleServer server)
        {
            _server = server;
        }

        private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent content = null, bool delta = false)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };
            request.Headers.UserAgent.ParseAdd(Browser);
            if (delta)
            {
                request.Headers.Add("X-MicrosoftAjax", "Delta=true");
            }
            return _server.Client.SendAsync(request);
        }

        private async Task<string> GetPageAsync()
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Get, Url);
            return await response.Content.ReadAsStringAsync();
        }

        private static string HiddenField(string page, string name)
        {
            Match match = Regex.Match(page, "id=\"" + name + "\" value=\"([^\"]*)\"");
            Assert.True(match.Success, name + " not found");
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        private static FormUrlEncodedContent Postback(string page, bool async)
        {
            var fields = new Dictionary<string, string>
            {
                ["__EVENTTARGET"] = "",
                ["__EVENTARGUMENT"] = "",
                ["__VIEWSTATE"] = HiddenField(page, "__VIEWSTATE"),
                ["__VIEWSTATEGENERATOR"] = HiddenField(page, "__VIEWSTATEGENERATOR"),
                ["__EVENTVALIDATION"] = HiddenField(page, "__EVENTVALIDATION"),
                ["Amount"] = "7",
                ["Add"] = "Add",
            };
            if (async)
            {
                // What MicrosoftAjaxWebForms.js posts for a button inside the panel.
                fields["ScriptManager1"] = "Panel1|Add";
                fields["__ASYNCPOST"] = "true";
            }
            return new FormUrlEncodedContent(fields);
        }

        [Fact]
        public async Task ScriptManagerServesItsScripts()
        {
            string page = await GetPageAsync();

            Assert.Contains("Sys.WebForms.PageRequestManager._initialize('ScriptManager1', 'form1'", page);
            Assert.Contains("var PageMethods = function()", page); // EnablePageMethods: the proxy is inline

            MatchCollection scripts = Regex.Matches(page, "src=\"(/ScriptResource\\.axd\\?[^\"]+)\"");
            Assert.NotEmpty(scripts);
            var bodies = new List<string>();
            foreach (Match script in scripts)
            {
                HttpResponseMessage response = await SendAsync(HttpMethod.Get, WebUtility.HtmlDecode(script.Groups[1].Value));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("application/x-javascript", response.Content.Headers.ContentType?.MediaType);
                bodies.Add(await response.Content.ReadAsStringAsync());
            }
            Assert.Contains(bodies, b => b.Contains("Type.registerNamespace('Sys')") || b.Contains("Type.registerNamespace(\"Sys\")") || b.Contains("Sys.Application"));
            Assert.Contains(bodies, b => b.Contains("Sys.WebForms.PageRequestManager"));
        }

        [Fact]
        public async Task UpdatePanelPartialPostback()
        {
            string page = await GetPageAsync();

            HttpResponseMessage response = await SendAsync(HttpMethod.Post, Url, Postback(page, async: true), delta: true);
            string delta = await response.Content.ReadAsStringAsync();

            // The delta format: length|type|id|content| for the panel, view state and the other hidden fields.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
            Assert.Matches(@"^1\|#\|\|4\|\d+\|updatePanel\|Panel1\|", delta);
            Assert.Contains(">17</span>;mode=<span id=\"Mode\">async</span>", delta);
            Assert.Contains("|hiddenField|__VIEWSTATE|", delta);
            Assert.DoesNotContain("outside", delta); // only the panel is rendered
        }

        [Fact]
        public async Task FullPostbackStillWorks()
        {
            string page = await GetPageAsync();

            HttpResponseMessage response = await SendAsync(HttpMethod.Post, Url, Postback(page, async: false));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(">17</span>;mode=<span id=\"Mode\">full</span>", body);
            Assert.Contains("outside", body);
        }

        [Fact]
        public async Task PageMethodAnswersJson()
        {
            HttpResponseMessage response = await SendAsync(HttpMethod.Post, Url + "/Greet",
                new StringContent("{\"name\":\"Ada\"}", Encoding.UTF8, "application/json"));
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"d\":\"Hello, Ada\"}", body);
        }
    }
}
