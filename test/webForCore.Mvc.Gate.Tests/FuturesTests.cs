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
        public async Task MvcSerializerIsNotSupported()
        {
            // Html.Serialize and [Deserialize] use NetDataContractSerializer, which .NET does not have.
            string body = await _server.Client.GetStringAsync("/Futures/Serialize");

            Assert.StartsWith("failed: System.", body);
        }
    }
}
