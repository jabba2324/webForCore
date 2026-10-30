using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace MvcGate
{
    // Runs test/webForCore.Mvc.Sample (built by the project reference) on Kestrel from its project folder, where
    // the fork's System.Web finds Web.config and Global.asax.
    public sealed class SampleServer : SampleServerBase
    {
        public SampleServer() : base("webForCore.Mvc.Sample")
        {
        }
    }

    // One MVC sample process for all its test classes: processes running one app folder share its compilation output
    // (Temporary ASP.NET Files), and one compiling views makes another restart (503).
    [CollectionDefinition(Name)]
    public class MvcSampleCollection : ICollectionFixture<SampleServer>
    {
        public const string Name = "MVC sample";
    }

    // The same for test/webForCore.WebPages.Sample (an ASP.NET Web Pages site).
    public sealed class WebPagesServer : SampleServerBase
    {
        public WebPagesServer() : base("webForCore.WebPages.Sample")
        {
        }
    }

    public abstract class SampleServerBase : IDisposable
    {
        private readonly Process _process;

        protected SampleServerBase(string sample)
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "test", sample)))
            {
                dir = Path.GetDirectoryName(dir);
            }
            SampleDirectory = Path.Combine(dir ?? throw new InvalidOperationException("repo root not found"), "test", sample);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            BaseAddress = new Uri($"http://127.0.0.1:{port}");

            var start = new ProcessStartInfo("dotnet", $"bin_dotnet/{sample}.dll --urls {BaseAddress}")
            {
                WorkingDirectory = SampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { };
            _process.ErrorDataReceived += (s, e) => { };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = BaseAddress };
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (true)
            {
                try
                {
                    // The first request starts the ASP.NET application (Application_Start, OWIN startup).
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

        public HttpClient Client { get; }

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
