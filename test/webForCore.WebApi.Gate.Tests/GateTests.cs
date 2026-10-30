using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web.Http;
using System.Web.Http.Hosting;
using Xunit;

namespace WebApiGate
{
    // Each case runs against the sample on Kestrel, once through GlobalConfiguration (System.Web.Http.WebHost) and once
    // through OWIN under /owin (Microsoft.Owin.Host.SystemWeb, System.Web.Http.Owin), and against the oracle: the same
    // controllers and WebApiConfig in Web API's in-memory HttpServer, which involves no System.Web. Status, media type,
    // charset, body, Location and Allow must match the oracle. The headers ASP.NET adds are checked against what
    // System.Web on IIS sends. Every response is also written to gate-responses.json (beside the test assembly), for a
    // diff against the same requests on IIS.
    public class GateTests : IClassFixture<SampleServer>
    {
        public static readonly string[] Hosts = { "", "/owin" };

        public static readonly object[][] Cases =
        {
            // The Visual Studio "Web API" template
            Case("GET", "/api/values"),
            Case("GET", "/api/values/5"),
            Case("POST", "/api/values", "\"posted\""),
            Case("PUT", "/api/values/5", "\"put\""),
            Case("DELETE", "/api/values/5"),
            // "Getting Started with ASP.NET Web API 2"
            Case("GET", "/api/products"),
            Case("GET", "/api/products/2"),
            Case("GET", "/api/products/1", accept: "application/xml"),
            Case("GET", "/api/products/1", accept: "text/xml"),
            Case("GET", "/api/products/99"),
            Case("POST", "/api/products", "{\"Name\":\"Widget\",\"Category\":\"Tools\",\"Price\":5}"),
            Case("POST", "/api/products", "{\"Price\":5000}"),
            Case("POST", "/api/products", "{\"Name\":", accept: null),
            Case("POST", "/api/products", "<Product xmlns=\"http://schemas.datacontract.org/2004/07/WebApiSample.Models\"><Name>Xml</Name><Price>2</Price></Product>",
                 contentType: "application/xml"),
            Case("PUT", "/api/products/1", "{}"),
            Case("GET", "/api/nothere"),
            // StatusController
            Case("GET", "/api/status/ok"),
            Case("GET", "/api/status/nocontent"),
            Case("GET", "/api/status/redirect"),
            Case("GET", "/api/status/unauthorized"),
            Case("GET", "/api/status/badrequest"),
            Case("GET", "/api/status/conflict"),
            Case("GET", "/api/status/httperror"),
            Case("GET", "/api/status/throwsresponse"),
            Case("GET", "/api/status/throws"),
            Case("GET", "/api/status/item/7"),
            Case("GET", "/api/status/text"),
            Case("GET", "/api/status/query?name=a%20b&count=3"),
            Case("GET", "/api/status/query"),
            Case("POST", "/api/status/echo", "{\"Id\":9,\"Name\":\"Ünïcödé €\",\"Price\":1.5}"),
            Case("HEAD", "/api/values"),
            Case("OPTIONS", "/api/values"),
        };

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, object> Recorded = new Dictionary<string, object>();

        private readonly SampleServer _server;

        public GateTests(SampleServer server)
        {
            _server = server;
        }

        public static IEnumerable<object[]> HostsAndCases()
        {
            foreach (string host in Hosts)
                foreach (object[] c in Cases)
                    yield return new object[] { host == "" ? "webhost" : "owin", c[0], c[1], c[2], c[3], c[4] };
        }

        private static object[] Case(string method, string path, string body = null, string accept = "application/json",
            string contentType = "application/json")
        {
            return new object[] { method, path, body, accept, body == null ? null : contentType };
        }

        [Theory]
        [MemberData(nameof(HostsAndCases))]
        public async Task SampleOnKestrelMatchesWebApi(string host, string method, string path, string body, string accept, string contentType)
        {
            string prefix = host == "owin" ? "/owin" : "";
            HttpResponseMessage actual = await _server.Client.SendAsync(CreateRequest(new Uri(_server.BaseAddress, prefix + path), method, body, accept, contentType));
            HttpResponseMessage expected = await SendToOracle(prefix, CreateRequest(new Uri(_server.BaseAddress, prefix + path), method, body, accept, contentType));

            string actualBody = await actual.Content.ReadAsStringAsync();
            // A server sends no body for HEAD but keeps the content headers.
            string expectedBody = method == "HEAD" ? "" : await expected.Content.ReadAsStringAsync();
            Record($"{host} {method} {path} accept={accept} {contentType} {body}".TrimEnd(), actual, actualBody);

            Assert.Equal((int)expected.StatusCode, (int)actual.StatusCode);
            Assert.Equal(expected.Content.Headers.ContentType?.MediaType, actual.Content.Headers.ContentType?.MediaType);
            Assert.Equal(expected.Content.Headers.ContentType?.CharSet, actual.Content.Headers.ContentType?.CharSet);
            Assert.Equal(expected.Headers.Location, actual.Headers.Location);
            Assert.Equal(string.Join(",", expected.Content.Headers.Allow), string.Join(",", actual.Content.Headers.Allow));
            if (method == "HEAD")
            {
                Assert.Equal("", actualBody);
            }
            else if (path == "/api/status/throws")
            {
                // Error detail: the oracle request is marked local, as a request from 127.0.0.1 is under System.Web.
                // The stack traces differ by host; the rest of the HttpError must match.
                Assert.Equal(WithoutStackTrace(expectedBody), WithoutStackTrace(actualBody));
                Assert.Contains("WebApiSample.Controllers.StatusController.GetThrows()", actualBody);
            }
            else
            {
                Assert.Equal(expectedBody, actualBody);
            }

            // Headers added by System.Web (as on IIS): Web API's WebHost marks responses that set no caching as
            // no-cache; elsewhere ASP.NET's default is Cache-Control: private.
            Assert.Contains(actual.Headers.GetValues("X-AspNet-Version"), v => v == "4.0.30319");
            if (host == "webhost")
            {
                Assert.Equal("no-cache", actual.Headers.CacheControl.ToString());
                Assert.Equal("no-cache", string.Join(",", actual.Headers.Pragma));
                Assert.Equal("-1", string.Join(",", actual.Content.Headers.GetValues("Expires")));
            }
            else
            {
                Assert.Equal("private", actual.Headers.CacheControl.ToString());
            }
        }

        [Theory]
        [InlineData("/api/status/item/abc")]
        [InlineData("/owin/api/status/item/abc")]
        public async Task UnmatchedRouteIsNotFound(string path)
        {
            // No Web API route matches (the {id:int} constraint fails), so ASP.NET answers 404, not Web API.
            HttpResponseMessage response = await _server.Client.GetAsync(path);
            Record($"system.web GET {path}", response, null);
            Assert.Equal(404, (int)response.StatusCode);
        }

        private static HttpRequestMessage CreateRequest(Uri uri, string method, string body, string accept, string contentType)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), uri);
            if (accept != null)
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            }
            if (body != null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, contentType);
            }
            return request;
        }

        private static readonly Dictionary<string, HttpServer> Oracles = new Dictionary<string, HttpServer>();

        private static async Task<HttpResponseMessage> SendToOracle(string prefix, HttpRequestMessage request)
        {
            HttpServer server;
            lock (Oracles)
            {
                if (!Oracles.TryGetValue(prefix, out server))
                {
                    var config = new HttpConfiguration(new HttpRouteCollection(prefix == "" ? "/" : prefix));
                    WebApiSample.WebApiConfig.Register(config);
                    config.EnsureInitialized();
                    Oracles[prefix] = server = new HttpServer(config);
                }
            }
            request.Properties[HttpPropertyKeys.IsLocalKey] = new Lazy<bool>(() => true);
            using (var invoker = new HttpMessageInvoker(server, disposeHandler: false))
            {
                HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
                return response;
            }
        }

        private static string WithoutStackTrace(string json)
        {
            JsonNode node = JsonNode.Parse(json);
            for (JsonObject o = node.AsObject(); o != null; o = o["InnerException"] as JsonObject)
            {
                o.Remove("StackTrace");
            }
            return node.ToJsonString();
        }

        private static void Record(string key, HttpResponseMessage response, string body)
        {
            var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers.Concat(response.Content.Headers))
            {
                if (h.Key != "Date" && h.Key != "Server")
                {
                    headers[h.Key] = string.Join(", ", h.Value);
                }
            }
            lock (Gate)
            {
                Recorded[key] = new { status = (int)response.StatusCode, headers, body };
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gate-responses.json"),
                    JsonSerializer.Serialize(new SortedDictionary<string, object>(Recorded), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }
}
