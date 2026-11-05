using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WebForCoreGate;
using Xunit;

namespace IISGate
{
    // Request handling as IIS 10 does it (#126, #127, #129, #140), on test/webForCore.Requests.Sample: a site of pages,
    // App_Code and static files that IIS serves as it is. Every response is written to requests-responses.json beside the
    // test assembly, and CI's iis-compare job sends the same requests to the folder on IIS 10 (.NET Framework 4.8, classic
    // and integrated pipelines) and diffs status, headers and body (tools/ci/iis_compare.cs). The values asserted here
    // are IIS's, from those runs.
    [Collection(RequestsSampleCollection.Name)]
    public class RequestHandlingTests
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, object> Recorded = new Dictionary<string, object>();

        private readonly RequestsSampleServer _server;

        public RequestHandlingTests(RequestsSampleServer server)
        {
            _server = server;
        }

        // Each request and IIS's status for it.
        public static readonly TheoryData<string, int> Requests = new TheoryData<string, int>
        {
            // Paths, path info and query strings (Echo.aspx writes the request's paths).
            { "/Echo.aspx", 200 },
            { "/Echo.aspx?x=1&y=a%20b", 200 },
            { "/Echo.aspx?q=%25", 200 },
            { "/Echo.aspx/extra/path", 200 },
            { "/Echo.aspx/a.b/c", 200 },
            { "/Echo.aspx/x.aspx", 200 },
            { "/echo.aspx/X", 200 },
            { "/ECHO.ASPX", 200 },
            // #126: escaped characters
            { "/Echo.aspx/a%20b", 200 },
            { "/Echo%20Page.aspx", 200 },
            { "/Echo%20Page.aspx/a%20b", 200 },
            { "/Echo.aspx/%C3%BC", 200 },
            { "/Product/Convertible%20Car", 200 },
            { "/Product/%C3%9Cn%C3%AF", 200 },
            { "/Content/a%20b.txt", 200 },
            { "/Content/a%20b.htm", 200 },
            { "/Content/%C3%9Cn%C3%AF.txt", 200 },
            { "/Content/missing%20file.txt", 404 },
            { "/About%20x", 404 },
            // ... the characters of <httpRuntime requestPathInvalidCharacters> (ASP.NET's 400), double escapes and '+'
            // (request filtering's 404.11), and the rest, which http.sys normalises.
            { "/Echo.aspx/a%25b", 400 },
            { "/Echo.aspx/a%26b", 400 },
            { "/Echo.aspx/a%3Fb", 400 },
            { "/Echo.aspx/a%3Ab", 400 },
            { "/Echo.aspx/a:b", 400 },
            { "/Echo.aspx/a%3Cb", 400 },
            { "/Echo.aspx/a*b", 400 },
            { "/Echo.aspx/a%2525b", 404 },
            { "/Echo.aspx/a+b", 404 },
            { "/Echo.aspx/a%2Bb", 404 },
            { "/Echo.aspx/a%2Fb", 200 },
            { "/Echo.aspx/a%5Cb", 200 },
            { "/Echo.aspx/a%7Eb", 200 },
            { "/Echo.aspx/a;b", 200 },
            { "/Echo.aspx/a//b", 200 },
            { "//Echo.aspx", 200 },
            { "/x/%2E%2E/Echo.aspx", 200 },
            // #127: default documents
            { "/", 200 },
            { "/?q=1", 200 },
            { "/dir/", 200 },
            { "/dir/?q=1", 200 },
            { "/DIR/", 200 },
            { "/dir", 301 },
            { "/dir/Default.aspx", 200 },
            // #140: static files in another case
            { "/Content/Mixed.txt", 200 },
            { "/content/mixed.txt", 200 },
            { "/CONTENT/MIXED.TXT", 200 },
            { "/Content/Sub/Deep.txt", 200 },
            { "/content/sub/deep.txt", 200 },
            { "/content/nothere.txt", 404 },
            // #129: Response.End, Redirect, Server.Transfer and CompleteRequest, then EndRequest
            { "/end/Status401.aspx", 302 },
            { "/end/End401.aspx", 302 },
            { "/end/Complete401.aspx", 302 },
            { "/end/End.aspx", 200 },
            { "/end/Redirect.aspx", 302 },
            { "/end/RedirectNoEnd.aspx", 302 },
            { "/end/Transfer.aspx", 200 },
            { "/end/Complete.aspx", 200 },
            { "/end/FlushEnd.aspx", 200 },
            // #184: Response.Write with BufferOutput = false
            { "/unbuffered/Write.aspx", 200 },
            { "/unbuffered/Stream.aspx", 200 },
            { "/Login.aspx", 200 },
            // The integrated pipeline's URL server variables (none in the classic pipeline, IIS's or webForCore's).
            { "/ServerVariables.aspx", 200 },
        };

        [Theory]
        [MemberData(nameof(Requests))]
        public async Task StatusIsIISs(string path, int status)
        {
            var (response, _) = await Get(path);
            Assert.Equal(status, (int)response.StatusCode);
        }

        // #184 (Bonobo Git Server: git's smart HTTP never answered): Response.Write with BufferOutput = false, and then
        // copies to Response.OutputStream, are sent, chunked, and the response ends.
        [Fact]
        public async Task UnbufferedWritesAreSent()
        {
            var (write, body) = await Get("/unbuffered/Write.aspx");
            Assert.Equal(HttpStatusCode.OK, write.StatusCode);
            Assert.Equal("hello world\n", body);
            Assert.True(write.Headers.TransferEncodingChunked);
            var (stream, streamed) = await Get("/unbuffered/Stream.aspx");
            Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
            var data = new char[10000];
            for (int i = 0; i < data.Length; i++) data[i] = i % 64 == 63 ? '\n' : (char)('a' + i % 26);
            Assert.Equal("001e# service=git-upload-pack\n" + new string(data) + "|end\n", streamed);
        }

        // #126 (Wingtip Toys: every product link, /Product/Convertible%20Car, was an empty 400): the request reaches the
        // application with the decoded path, as IIS gives it.
        [Fact]
        public async Task AnEscapedPathReachesTheApplicationDecoded()
        {
            var (product, body) = await Get("/Product/Convertible%20Car");
            Assert.Equal(HttpStatusCode.OK, product.StatusCode);
            Assert.Contains("Path: [/Product/Convertible Car]\n", body);
            Assert.Contains("FilePath: [/Product/Convertible Car]\n", body);
            Assert.Contains("RawUrl: [/Product/Convertible Car]\n", body);
            Assert.Contains("Url.AbsolutePath: [/Product/Convertible%20Car]\n", body);
            Assert.Contains("CurrentExecutionFilePath: [/Product/Convertible Car]\n", body);
            Assert.Contains("Route productName: [Convertible Car]\n", body);

            var (_, page) = await Get("/Echo%20Page.aspx/a%20b");
            Assert.Contains("Path: [/Echo Page.aspx/a b]\n", page);
            Assert.Contains("FilePath: [/Echo Page.aspx]\n", page);
            Assert.Contains("PathInfo: [/a b]\n", page);
            Assert.Contains("RawUrl: [/Echo Page.aspx/a b]\n", page);
            Assert.Contains("PATH_INFO: [/Echo Page.aspx/a b]\n", page);
            Assert.Contains("SCRIPT_NAME: [/Echo Page.aspx]\n", page);

            var (_, file) = await Get("/Content/a%20b.txt");
            Assert.Equal("a b\n", file);
            var (_, unicode) = await Get("/Content/%C3%9Cn%C3%AF.txt");
            Assert.Equal("non-ASCII\n", unicode);

            // http.sys decodes an escaped '/' and a '\', and merges slashes; the query string stays as it was sent.
            var (_, slash) = await Get("/Echo.aspx/a%2Fb?q=%25");
            Assert.Contains("Path: [/Echo.aspx/a/b]\n", slash);
            Assert.Contains("PathInfo: [/a/b]\n", slash);
            Assert.Contains("RawUrl: [/Echo.aspx/a/b?q=%25]\n", slash);
            var (_, doubled) = await Get("//Echo.aspx");
            Assert.Contains("Path: [/Echo.aspx]\n", doubled);
            Assert.Contains("RawUrl: [/Echo.aspx]\n", doubled);
        }

        // #126: what IIS rejects, it rejects with its own answers, not an empty 400.
        [Fact]
        public async Task InvalidPathsGetIISsAnswers()
        {
            var (percent, body) = await Get("/Echo.aspx/a%25b");
            Assert.Equal(HttpStatusCode.BadRequest, percent.StatusCode);
            Assert.Contains("A potentially dangerous Request.Path value was detected from the client (%).", body);
            var (colon, colonBody) = await Get("/Echo.aspx/a:b");
            Assert.Equal(HttpStatusCode.BadRequest, colon.StatusCode);
            Assert.Contains("A potentially dangerous Request.Path value was detected from the client (:).", colonBody);
            var (doubleEscape, doubleBody) = await Get("/Echo.aspx/a%2525b");
            Assert.Equal(HttpStatusCode.NotFound, doubleEscape.StatusCode);
            Assert.Contains("404.11", doubleBody);
        }

        // #126: the script is the first segment with a handler's extension; the rest is the path info.
        [Theory]
        [InlineData("/Echo.aspx/extra/path", "/Echo.aspx", "/extra/path")]
        [InlineData("/Echo.aspx/a.b/c", "/Echo.aspx", "/a.b/c")]
        [InlineData("/Echo.aspx/x.aspx", "/Echo.aspx", "/x.aspx")]
        [InlineData("/echo.aspx/X", "/echo.aspx", "/X")]
        public async Task PathInfoIsAfterTheScript(string path, string filePath, string pathInfo)
        {
            var (response, body) = await Get(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"FilePath: [{filePath}]\n", body);
            Assert.Contains($"PathInfo: [{pathInfo}]\n", body);
            Assert.Contains($"RawUrl: [{path}]\n", body);
        }

        // #127 (Wingtip Toys, the Visual Studio Web Forms template: FriendlyUrls redirected / to /Default, because RawUrl
        // contained the page's path): the default document is a rewrite on the server; RawUrl is the URL asked for.
        [Theory]
        [InlineData("/", "/default.aspx", "/")]
        [InlineData("/?q=1", "/default.aspx", "/?q=1")]
        [InlineData("/dir/", "/dir/default.aspx", "/dir/")]
        [InlineData("/dir/?q=1", "/dir/default.aspx", "/dir/?q=1")]
        [InlineData("/DIR/", "/DIR/default.aspx", "/DIR/")]
        public async Task ADirectorysRawUrlIsTheDirectory(string path, string page, string rawUrl)
        {
            var (response, body) = await Get(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"Path: [{page}]\n", body);
            Assert.Contains($"FilePath: [{page}]\n", body);
            Assert.Contains($"CurrentExecutionFilePath: [{page}]\n", body);
            Assert.Contains($"RawUrl: [{rawUrl}]\n", body);
            Assert.Contains($"Url.AbsolutePath: [{page}]\n", body);
        }

        // #127: IIS's courtesy redirect of a directory to its trailing slash is to an absolute URL.
        [Fact]
        public async Task ADirectoryIsRedirectedToItsSlashWithAnAbsoluteUrl()
        {
            var (response, body) = await Get("/dir");
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            var location = new Uri(_server.BaseAddress, "/dir/").AbsoluteUri;
            Assert.Equal(location, response.Headers.Location?.OriginalString);
            Assert.Contains($"<a HREF=\"{location}\">here</a>", body);
        }

        // #140 (nopCommerce 3.90: every product thumbnail, /content/images/thumbs/..., was 404 on Linux): a file asked for
        // in another case is found the first time, as on IIS's file system, not only once it has been asked for in its
        // own case. Order.txt is asked for only here.
        [Fact]
        public async Task AFileInAnotherCaseIsFoundTheFirstTime()
        {
            foreach (var path in new[] { "/content/order.txt", "/CONTENT/ORDER.TXT", "/Content/Order.txt", "/content/order.txt" })
            {
                var (response, body) = await Get(path, record: false);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {(int)response.StatusCode}");
                Assert.Equal("Order\n", body);
            }
            var (deep, deepBody) = await Get("/content/sub/deep.txt");
            Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
            Assert.Equal("Deep\n", deepBody);
        }

        // A 304 Not Modified has no Content-Length, as on IIS 10, which sends none for it; ASP.NET's own Content-Length: 0
        // isn't sent. A customer application's CacheCow.Server.WebApi answers If-None-Match with 304s (Packages.Gate.Tests,
        // compared with IIS 10 by iis-compare); here a page does.
        [Fact]
        public async Task A304HasNoContentLength()
        {
            var (response, body) = await Get("/status/NotModified.aspx");
            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Equal("", body);
            Assert.False(response.Content.Headers.Contains("Content-Length"));
        }

        // #129 (a WebMatrix Starter Site: WebSecurity.RequireAuthenticatedUser() answered an empty 401 instead of the
        // redirect to the log-in page): after Response.End() the response is still buffered when EndRequest runs, so
        // FormsAuthenticationModule turns the 401 into its redirect, and every module's EndRequest can change it.
        [Theory]
        [InlineData("/end/Status401.aspx")]
        [InlineData("/end/End401.aspx")]
        [InlineData("/end/Complete401.aspx")]
        public async Task A401BecomesTheRedirectToTheLogInPage(string path)
        {
            var (response, _) = await Get(path);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Equal("/Login.aspx?ReturnUrl=" + Uri.EscapeDataString(path).Replace("%2F", "%2f"), response.Headers.Location?.OriginalString);
            Assert.Equal("ran", Header(response, "X-End-Request"));
        }

        // #129: Response.End(), Redirect(url, true), Server.Transfer and CompleteRequest() end the handler's output as on
        // IIS, and EndRequest runs with the response still buffered; after Response.Flush() it is sent.
        [Theory]
        [InlineData("/end/End.aspx", 200, "before End\n", "ran")]
        [InlineData("/end/Complete.aspx", 200, "before CompleteRequest\nafter CompleteRequest\n", "ran")]
        [InlineData("/end/FlushEnd.aspx", 200, "before Flush\nafter Flush\n", null)]
        [InlineData("/end/Redirect.aspx", 302, null, "ran")]
        [InlineData("/end/RedirectNoEnd.aspx", 302, null, "ran")]
        [InlineData("/end/Transfer.aspx", 200, null, "ran")]
        public async Task EndRequestRunsAfterTheHandlerEnds(string path, int status, string body, string endRequest)
        {
            var (response, text) = await Get(path);
            Assert.Equal(status, (int)response.StatusCode);
            if (body != null) Assert.Equal(body, text);
            Assert.Equal(endRequest, Header(response, "X-End-Request"));
            if (status == 302)
            {
                Assert.Equal("/Echo.aspx", response.Headers.Location?.OriginalString);
                Assert.DoesNotContain("after Redirect", text);
            }
            if (path == "/end/Transfer.aspx")
            {
                Assert.StartsWith("before Transfer\nPath: [/end/Transfer.aspx]\n", text);
                Assert.Contains("CurrentExecutionFilePath: [/Echo.aspx]\n", text);
                Assert.DoesNotContain("after Transfer", text);
            }
        }

        // #170 (the smoke test's odata-samples-compositekey: a PATCH to /People(FirstName='Kate',LastName='Jones') with
        // Prefer: return=representation answered 204 instead of OData's 200 with the entity): a request whose path has
        // a '(' but isn't a cookieless one lost every header IIS has no index for (Prefer, X-Requested-With, X-...), in
        // Request.Headers and in ALL_RAW. IIS keeps them whatever the path.
        private static readonly Dictionary<string, string> UnknownHeaders = new Dictionary<string, string>
        {
            ["X-Custom"] = "one",
            ["Prefer"] = "return=representation",
            ["X-Requested-With"] = "XMLHttpRequest",
        };

        [Theory]
        [InlineData("/Headers.aspx")]
        [InlineData("/Headers.aspx/People(1)")]
        [InlineData("/Headers.aspx/People(FirstName='Kate',LastName='Jones')")]
        [InlineData("/Headers.aspx/a(1)/b")]
        [InlineData("/Headers.aspx/a)")]
        public async Task UnknownHeadersReachTheApplicationWhateverThePath(string path)
        {
            var (response, body) = await Get(path, requestHeaders: UnknownHeaders);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            foreach (var (name, value) in UnknownHeaders)
            {
                Assert.Contains($"{name}: [{value}]\n", body);
                Assert.Contains($"{name} in ALL_RAW: True\n", body);
            }
            Assert.Contains("AspFilterSessionId: (null)\n", body);
        }

        // #170: a cookieless path still gives its session id to the application, as AspFilterSessionId, and the page is
        // the one without it; the request's other headers are kept with it.
        [Fact]
        public async Task ACookielessPathKeepsItsSessionIdAndTheHeaders()
        {
            var (response, body) = await Get("/(S(abc))/Headers.aspx", requestHeaders: UnknownHeaders);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Path: [/Headers.aspx]\n", body);
            Assert.Contains("AspFilterSessionId: [S(abc)]\n", body);
            foreach (var (name, value) in UnknownHeaders)
                Assert.Contains($"{name}: [{value}]\n", body);
        }

        private static string Header(HttpResponseMessage response, string name) =>
            response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : null;

        private async Task<(HttpResponseMessage Response, string Body)> Get(string path, bool record = true, IDictionary<string, string> requestHeaders = null)
        {
            // The path as it is written, not as a relative URI ("//Echo.aspx" is a path here).
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_server.BaseAddress.GetLeftPart(UriPartial.Authority) + path));
            if (requestHeaders != null)
            {
                foreach (var (name, value) in requestHeaders) request.Headers.TryAddWithoutValidation(name, value);
            }
            var response = await _server.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            // A request with headers of its own is another request for the comparison with IIS.
            if (record) Record("GET " + path + (requestHeaders != null ? " (with headers)" : ""), response, body, requestHeaders);
            return (response, body);
        }

        // As WebApi.Gate.Tests records its requests for the comparison with IIS (#78); the request's own headers, if it
        // has any, are sent again on IIS (#170).
        private static void Record(string key, HttpResponseMessage response, string body, IDictionary<string, string> requestHeaders = null)
        {
            HttpRequestMessage sent = response.RequestMessage;
            var request = new
            {
                method = sent.Method.Method,
                url = sent.RequestUri.AbsoluteUri,
                accept = (string)null,
                contentType = (string)null,
                body = (string)null,
                headers = requestHeaders == null ? null : new SortedDictionary<string, string>(requestHeaders, StringComparer.OrdinalIgnoreCase),
            };
            var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in response.Headers.Concat(response.Content.Headers))
            {
                if (h.Key != "Date") headers[h.Key] = string.Join(", ", h.Value);
            }
            lock (Gate)
            {
                Recorded[key] = new { request, status = (int)response.StatusCode, headers, body };
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "requests-responses.json"),
                    JsonSerializer.Serialize(new SortedDictionary<string, object>(Recorded, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    // Runs test/webForCore.Requests.Sample under webforcore-host (#56) from its project folder.
    public sealed class RequestsSampleServer : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly string _tempDirectory;

        public RequestsSampleServer()
        {
            const string sample = "webForCore.Requests.Sample";
            string repository = WebForCoreHost.Repository(Path.Combine("test", sample));
            string sampleDirectory = Path.Combine(repository, "test", sample);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            listener.Stop();

            var start = WebForCoreHost.StartInfo(repository, sampleDirectory, sample, new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.ToString(),
            }, out _tempDirectory);
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
            {
                BaseAddress = BaseAddress,
            };
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline && !_process.HasExited)
            {
                try
                {
                    // 503 while the first worker starts; the first request starts the application.
                    if (Client.GetAsync("/Login.aspx").GetAwaiter().GetResult().StatusCode != HttpStatusCode.ServiceUnavailable) break;
                }
                catch (HttpRequestException)
                {
                }
                Thread.Sleep(250);
            }
        }

        public Uri BaseAddress { get; }

        public HttpClient Client { get; }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
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

    [CollectionDefinition(Name)]
    public class RequestsSampleCollection : ICollectionFixture<RequestsSampleServer>
    {
        public const string Name = "Requests sample";
    }
}
