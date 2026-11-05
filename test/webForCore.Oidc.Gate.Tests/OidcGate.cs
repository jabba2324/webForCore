using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using WebForCoreGate;
using WebForCoreOidc;

namespace OidcGate
{
    // Starts the provider and the sample, runs every scenario once, and keeps the responses for the tests.
    public sealed class OidcGate : IDisposable
    {
        private readonly Process _process;
        private readonly string _tempDirectory;
        private readonly System.Text.StringBuilder _output = new();

        public OidcGate()
        {
            string repository = WebForCoreHost.Repository(Path.Combine("test", "webForCore.Oidc.Sample"));
            string sample = Path.Combine(repository, "test", "webForCore.Oidc.Sample");
            AppBase = new Uri($"http://127.0.0.1:{FreePort()}/");
            ProviderBase = new Uri($"http://127.0.0.1:{FreePort()}/");
            Provider = OidcTestProvider.StartAsync(ProviderBase.GetLeftPart(UriPartial.Authority)).GetAwaiter().GetResult();

            // The template's ida:* settings, for this run's ports (Startup.Setting reads WFC_IDA_* first).
            var start = WebForCoreHost.StartInfo(repository, sample, "webForCore.Oidc.Sample", new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = AppBase.GetLeftPart(UriPartial.Authority),
                ["WFC_IDA_AUTHORITY"] = Provider.Issuer,
                ["WFC_IDA_REDIRECTURI"] = new Uri(AppBase, "/signin-oidc").AbsoluteUri,
                ["WFC_IDA_POSTLOGOUTREDIRECTURI"] = AppBase.AbsoluteUri,
            }, out _tempDirectory);
            _process = Process.Start(start)!;
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Responses = Scenarios.RunAllAsync(AppBase, ProviderBase).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "oidc-responses.json"), Scenarios.ToJson(Responses));
        }

        public Uri AppBase { get; }

        public Uri ProviderBase { get; }

        public OidcTestProvider Provider { get; }

        public SortedDictionary<string, JsonObject> Responses { get; }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        public JsonObject Response(string key) =>
            Responses.TryGetValue(key, out var response) ? response
                : throw new Xunit.Sdk.XunitException($"No response recorded for '{key}'. Recorded: {string.Join(", ", Responses.Keys)}\n{Output}");

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
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10000);
            }
            _process.Dispose();
            WebForCoreHost.DeleteTempDirectory(_tempDirectory);
            Provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
