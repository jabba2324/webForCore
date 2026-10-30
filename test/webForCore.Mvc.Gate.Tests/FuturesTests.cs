using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace MvcGate
{
    // MVC Futures (Microsoft.Web.Mvc) on the fork's MVC, through the sample's FuturesController.
    [Collection(MvcSampleCollection.Name)]
    public class FuturesTests
    {
        private readonly SampleServer _server;

        public FuturesTests(SampleServer server)
        {
            _server = server;
        }

        [Theory]
        [InlineData("/Futures/Csv", 200, "text/csv", "a,b")]
        [InlineData("/Futures/Link", 200, "text/html", "/Home/About /Products/Details/7")]
        [InlineData("/Futures/Copy", 200, "text/html", "3:Copied:9")]
        [InlineData("/Futures/Skip?name=x&other=y", 200, "text/html", "(null)/y")]
        [InlineData("/Futures/AjaxOnly", 404, "text/html", null)]
        public async Task Get(string path, int status, string mediaType, string body)
        {
            HttpResponseMessage response = await _server.Client.GetAsync(path);

            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
            if (body != null)
            {
                Assert.Equal(body, await response.Content.ReadAsStringAsync());
            }
        }

        [Fact]
        public async Task AjaxOnlyMatchesAjaxRequests()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/Futures/AjaxOnly");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");

            HttpResponseMessage response = await _server.Client.SendAsync(request);

            Assert.Equal("ajax", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task HtmlSerializeRoundTripsThroughTheForm()
        {
            // Html.Serialize puts the state in a hidden field, signed and encrypted with the machine key; [Deserialize]
            // reads it back from the post (webForCore's NetDataContractSerializer replacement under MvcSerializer).
            string page = await _server.Client.GetStringAsync("/Futures/Wizard");
            Match field = Regex.Match(page, "<input name=\"state\" type=\"hidden\" value=\"([^\"]+)\"");
            Assert.True(field.Success, page);
            string value = WebUtility.HtmlDecode(field.Groups[1].Value);
            Assert.DoesNotContain("Ada", value); // encrypted, not just encoded

            HttpResponseMessage response = await _server.Client.PostAsync("/Futures/Wizard",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["state"] = value }));

            Assert.Equal("step 2: Ada, red+green, 2026-10-30T12:00Z, Leeds/UK", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task DeserializeRejectsATamperedValue()
        {
            string page = await _server.Client.GetStringAsync("/Futures/Wizard");
            string value = WebUtility.HtmlDecode(Regex.Match(page, "<input name=\"state\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
            char[] chars = value.ToCharArray();
            chars[chars.Length / 2] = chars[chars.Length / 2] == 'A' ? 'B' : 'A';

            HttpResponseMessage response = await _server.Client.PostAsync("/Futures/Wizard",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["state"] = new string(chars) }));

            // As on .NET Framework, [Deserialize] lets MvcSerializer's SerializationException through: the request fails.
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Deserialization failed.", await response.Content.ReadAsStringAsync());
        }
    }
}
