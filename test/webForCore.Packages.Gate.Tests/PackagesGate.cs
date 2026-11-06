using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WebForCoreGate;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PackagesGate
{
    [CollectionDefinition(Name)]
    public class PackagesCollection : ICollectionFixture<PackagesGate>
    {
        public const string Name = "Packages sample";
    }

    // Starts test/webForCore.Packages.Sample under webforcore-host, sends the requests of Requests() once, in order, and
    // keeps the responses for the tests. They are written to packages-responses.json with each request as it was sent, in
    // tools/ci/iis_compare.cs's form; the keys start with a sequence number, so that its replay (in key order) sends them
    // to IIS in the same order. A request can depend on an earlier response (an ETag), so they are built as they go.
    public sealed class PackagesGate : IDisposable
    {
        private readonly Process _process;
        private readonly string _tempDirectory;
        private readonly StringBuilder _output = new();
        private readonly SortedDictionary<string, JsonObject> _responses = new(StringComparer.Ordinal);
        private int _sequence;

        public PackagesGate()
        {
            string repository = WebForCoreHost.Repository(Path.Combine("test", "webForCore.Packages.Sample"));
            SampleDirectory = Path.Combine(repository, "test", "webForCore.Packages.Sample");
            BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}/");
            var start = WebForCoreHost.StartInfo(repository, SampleDirectory, "webForCore.Packages.Sample", new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.GetLeftPart(UriPartial.Authority),
            }, out _tempDirectory);
            TempDirectory = _tempDirectory;
            _process = Process.Start(start)!;
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(60) };
            // The first request starts the application, as iis_compare.cs's replay does on IIS.
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (true)
            {
                try
                {
                    Client.GetAsync("/api/values").GetAwaiter().GetResult();
                    break;
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                    Thread.Sleep(250);
                }
            }
            RunAsync().GetAwaiter().GetResult();
            var all = new JsonObject();
            foreach (var (key, value) in _responses) all[key] = value.DeepClone();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "packages-responses.json"), all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public string SampleDirectory { get; }

        public string TempDirectory { get; }

        public Uri BaseAddress { get; }

        public HttpClient Client { get; }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        public IReadOnlyDictionary<string, JsonObject> Responses => _responses;

        // The response recorded for a request (its key without the sequence number), the first one or a later one.
        public JsonObject Response(string request, int occurrence = 1)
        {
            foreach (var (key, value) in _responses)
                if (key.Substring(key.IndexOf(' ') + 1) == request && --occurrence == 0) return value;
            throw new Xunit.Sdk.XunitException($"No response recorded for '{request}'. Recorded: {string.Join(", ", _responses.Keys)}\n{Output}");
        }

        public static JsonNode Json(JsonObject response) => JsonNode.Parse((string)response["body"]);

        public static string Header(JsonObject response, string name) =>
            response["headers"]!.AsObject().FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.GetValue<string>();

        private async Task RunAsync()
        {
            // Ninject.Web.Common.WebHost: twice, so that the second sees the first's request scope disposed.
            await SendAsync("GET", "/Scope.ashx");
            await SendAsync("GET", "/Scope.ashx");
            // An OWIN middleware resolving from the kernel in Ninject.Web.Common.OwinHost's request scope.
            await SendAsync("GET", "/owin/scope");
            await SendAsync("GET", "/owin/scope");
            // Web API controllers constructed by Ninject (UseNinjectWebApi).
            await SendAsync("GET", "/api/greeting");
            await SendAsync("GET", "/api/greeting?name=webForCore");

            // Swashbuckle.Core: the document, the UI and every file the UI's page loads.
            await SendAsync("GET", "/swagger/docs/v1");
            await SendAsync("GET", "/swagger");
            var index = await SendAsync("GET", "/swagger/ui/index");
            foreach (string asset in SwaggerUiAssets((string)index["body"]))
                await SendAsync("GET", "/swagger/ui/" + asset);

            // CacheCow.Server.WebApi: ETag, 304 for If-None-Match, and a new ETag after a PUT or a POST.
            var product = await SendAsync("GET", "/api/products/1");
            string etag = Header(product, "ETag");
            await SendAsync("GET", "/api/products/1", headers: IfNoneMatch(etag), name: "/api/products/1 If-None-Match (current)");
            await SendAsync("PUT", "/api/products/1", "{\"Name\":\"Tomato Soup\",\"Price\":1.25}");
            var changed = await SendAsync("GET", "/api/products/1", headers: IfNoneMatch(etag), name: "/api/products/1 If-None-Match (before the PUT)");
            await SendAsync("GET", "/api/products/1", headers: IfNoneMatch(Header(changed, "ETag")), name: "/api/products/1 If-None-Match (after the PUT)");
            var list = await SendAsync("GET", "/api/products");
            string listTag = Header(list, "ETag");
            await SendAsync("GET", "/api/products", headers: IfNoneMatch(listTag), name: "/api/products If-None-Match (current)");
            await SendAsync("POST", "/api/products", "{\"Name\":\"Hammer\",\"Price\":16.99}");
            await SendAsync("GET", "/api/products", headers: IfNoneMatch(listTag), name: "/api/products If-None-Match (before the POST)");
        }

        // The files Swashbuckle's index page loads (scripts, styles, images), outside HTML comments.
        public static IEnumerable<string> SwaggerUiAssets(string html) =>
            Regex.Matches(Regex.Replace(html, "<!--.*?-->", "", RegexOptions.Singleline), "(?:href|src)=['\"]([^'\"#:]+)['\"]")
                .Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);

        private static Dictionary<string, string> IfNoneMatch(string etag) => new() { ["If-None-Match"] = etag ?? "\"none\"" };

        private async Task<JsonObject> SendAsync(string method, string path, string body = null, Dictionary<string, string> headers = null, string name = null)
        {
            var message = new HttpRequestMessage(new HttpMethod(method), new Uri(BaseAddress, path));
            if (headers != null)
                foreach (var (header, value) in headers) message.Headers.TryAddWithoutValidation(header, value);
            if (body != null) message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await Client.SendAsync(message);
            string responseBody = await response.Content.ReadAsStringAsync();
            var responseHeaders = new JsonObject();
            foreach (var h in response.Headers.Concat(response.Content.Headers).OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
                if (h.Key != "Date") responseHeaders[h.Key] = string.Join(", ", h.Value);
            JsonObject requestHeaders = null;
            if (headers != null)
            {
                requestHeaders = new JsonObject();
                foreach (var (header, value) in headers) requestHeaders[header] = value;
            }
            var entry = new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["method"] = method,
                    ["url"] = message.RequestUri!.AbsoluteUri,
                    ["accept"] = null,
                    ["contentType"] = message.Content?.Headers.ContentType?.ToString(),
                    ["body"] = body,
                    ["headers"] = requestHeaders,
                },
                ["status"] = (int)response.StatusCode,
                ["headers"] = responseHeaders,
                ["body"] = responseBody,
            };
            _responses[$"{++_sequence:D3} {method} {name ?? path}"] = entry;
            return entry;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            Client.Dispose();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10000);
            }
            _process.Dispose();
            WebForCoreHost.DeleteTempDirectory(_tempDirectory);
        }
    }
}
