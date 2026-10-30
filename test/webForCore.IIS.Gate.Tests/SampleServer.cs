using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace IISGate
{
    // Runs test/webForCore.IIS.Sample on Kestrel from its project folder and keeps its console output (the hosting
    // layer's log: application starts, shutdowns, unloads).
    public sealed class SampleServer : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();

        public SampleServer()
        {
            const string sample = "webForCore.IIS.Sample";
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "test", sample)))
            {
                dir = Path.GetDirectoryName(dir);
            }
            SampleDirectory = Path.Combine(dir ?? throw new InvalidOperationException("repo root not found"), "test", sample);
            var logs = Path.Combine(SampleDirectory, "App_Data", "logs");
            if (Directory.Exists(logs))
            {
                Directory.Delete(logs, recursive: true);
            }

            BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}");
            HttpsAddress = new Uri($"https://127.0.0.1:{FreePort()}");

            var start = new ProcessStartInfo("dotnet", $"bin_dotnet/{sample}.dll --urls {BaseAddress}")
            {
                WorkingDirectory = SampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["Logging__LogLevel__Microsoft.AspNetCore.Builder"] = "Debug";
            start.Environment["GATE_HTTPS_PORT"] = HttpsAddress.Port.ToString();
            // The test key container and DPAPI key the sample's protected sections were encrypted with (App_Data is hidden).
            start.Environment["WEBFORCORE_KEYS"] = Path.Combine(SampleDirectory, "App_Data", "keys");
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
            {
                BaseAddress = BaseAddress,
            };
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                try
                {
                    Client.GetAsync("/").GetAwaiter().GetResult();
                    break;
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                    Thread.Sleep(250);
                }
            }
        }

        public string SampleDirectory { get; }

        public Uri BaseAddress { get; }

        // TLS with a generated server certificate; client certificates are accepted.
        public Uri HttpsAddress { get; }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

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
        }
    }

    [CollectionDefinition(Name)]
    public class IISSampleCollection : ICollectionFixture<SampleServer>
    {
        public const string Name = "IIS sample";
    }
}
