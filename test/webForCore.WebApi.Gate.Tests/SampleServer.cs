using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace WebApiGate
{
    // Runs test/webForCore.WebApi.Sample (built by the project reference) on Kestrel from its project folder, where
    // the fork's System.Web finds Web.config and Global.asax.
    public sealed class SampleServer : IDisposable
    {
        private readonly Process _process;
        private readonly System.Text.StringBuilder _output = new System.Text.StringBuilder();

        public SampleServer()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "test", "webForCore.WebApi.Sample")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            SampleDirectory = Path.Combine(dir ?? throw new InvalidOperationException("repo root not found"), "test", "webForCore.WebApi.Sample");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            BaseAddress = new Uri($"http://127.0.0.1:{port}");

            var start = new ProcessStartInfo("dotnet", $"bin_dotnet/webForCore.WebApi.Sample.dll --urls {BaseAddress}")
            {
                WorkingDirectory = SampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["GATE_RECYCLE_CHECK"] = "2";
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = BaseAddress };
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                try
                {
                    // The first request starts the ASP.NET application (Application_Start, OWIN startup).
                    Client.GetAsync("/api/values").GetAwaiter().GetResult();
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

        public HttpClient Client { get; }

        // The host's console log (application starts, shutdowns, unloads).
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
}
