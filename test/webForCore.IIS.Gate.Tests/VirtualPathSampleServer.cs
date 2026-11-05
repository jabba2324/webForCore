using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WebForCoreGate;
using Xunit;

namespace IISGate
{
    // Runs test/webForCore.VirtualPath.Sample, the application /app (#190), under webforcore-host (#56) from its project
    // folder, with the host's and the application's settings a test gives it (ForwardedHeadersTests, #194).
    public sealed class VirtualPathSampleServer : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly string _tempDirectory;

        public VirtualPathSampleServer()
            : this(new Dictionary<string, string>())
        {
        }

        internal VirtualPathSampleServer(IDictionary<string, string> settings)
        {
            const string sample = "webForCore.VirtualPath.Sample";
            string repository = WebForCoreHost.Repository(Path.Combine("test", sample));
            string sampleDirectory = Path.Combine(repository, "test", sample);
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
            listener.Stop();

            var environment = new Dictionary<string, string>(settings)
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.ToString(),
            };
            var start = WebForCoreHost.StartInfo(repository, sampleDirectory, sample, environment, out _tempDirectory);
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
            {
                BaseAddress = BaseAddress,
            };
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !_process.HasExited)
            {
                try
                {
                    // 503 while the first worker starts; the first request starts the application.
                    if (Client.GetAsync("/app/Probe.aspx").GetAwaiter().GetResult().StatusCode != HttpStatusCode.ServiceUnavailable) break;
                }
                catch (HttpRequestException)
                {
                }
                Thread.Sleep(250);
            }
        }

        public Uri BaseAddress { get; }

        public HttpClient Client { get; }

        // Once it has exited, after its output has been read.
        public bool HasExited
        {
            get
            {
                if (!_process.HasExited) return false;
                _process.WaitForExit();
                return true;
            }
        }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        // GET of a path as it is written, with the request's own headers.
        public async Task<(HttpResponseMessage Response, string Body)> Get(string path, IDictionary<string, string> headers = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress.GetLeftPart(UriPartial.Authority) + path));
            if (headers != null)
            {
                foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
            }
            var response = await Client.SendAsync(request);
            return (response, await response.Content.ReadAsStringAsync());
        }

        // The Probe page's "name=value" lines.
        public static Dictionary<string, string> Values(string body) =>
            body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2).GroupBy(parts => parts[0]).ToDictionary(g => g.Key, g => g.First()[1]);

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
    public class VirtualPathSampleCollection : ICollectionFixture<VirtualPathSampleServer>
    {
        public const string Name = "Virtual path sample";
    }
}
