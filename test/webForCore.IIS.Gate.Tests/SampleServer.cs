using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WebForCoreGate;
using Xunit;

namespace IISGate
{
    // Runs test/webForCore.IIS.Sample under webforcore-host (#56) from its project folder, with HTTP and HTTPS bindings
    // (a generated server certificate; client certificates accepted, as IIS's SSL setting "Accept"), and keeps the
    // console output of the host and its workers (worker starts, recycles, the application's shutdowns).
    public sealed class SampleServer : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly string _certificateFile;
        private readonly string _tempDirectory;

        public SampleServer()
        {
            const string sample = "webForCore.IIS.Sample";
            string repository = WebForCoreHost.Repository(Path.Combine("test", sample));
            SampleDirectory = Path.Combine(repository, "test", sample);
            var logs = Path.Combine(SampleDirectory, "w3c-logs");
            if (Directory.Exists(logs))
            {
                Directory.Delete(logs, recursive: true);
            }

            BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}");
            HttpsAddress = new Uri($"https://127.0.0.1:{FreePort()}");
            _certificateFile = Path.Combine(Path.GetTempPath(), $"webforcore-gate-{Guid.NewGuid():N}.pfx");
            const string password = "gate";
            File.WriteAllBytes(_certificateFile, ServerCertificate(password));

            var start = WebForCoreHost.StartInfo(repository, SampleDirectory, sample, new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.ToString(),
                ["Kestrel__Endpoints__Https__Url"] = HttpsAddress.ToString(),
                ["Kestrel__Endpoints__Https__Certificate__Path"] = _certificateFile,
                ["Kestrel__Endpoints__Https__Certificate__Password"] = password,
                ["Kestrel__Endpoints__Https__ClientCertificateMode"] = "AllowCertificate",
                // The tests' client certificate is self-signed: the application sees it, untrusted (CERT_FLAGS 3).
                ["WebForCore__Host__AcceptUntrustedClientCertificates"] = "true",
                ["Logging__LogLevel__Microsoft.AspNetCore.Builder"] = "Debug",
                ["GATE_RECYCLE_CHECK"] = "2",
                // The test key container and DPAPI key the sample's protected sections were encrypted with (App_Data is hidden).
                ["WEBFORCORE_KEYS"] = Path.Combine(SampleDirectory, "App_Data", "keys"),
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
            while (true)
            {
                try
                {
                    // 503 while the first worker starts.
                    if (Client.GetAsync("/").GetAwaiter().GetResult().StatusCode != HttpStatusCode.ServiceUnavailable) break;
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                }
                if (DateTime.UtcNow >= deadline || _process.HasExited) break;
                Thread.Sleep(250);
            }
        }

        public string SampleDirectory { get; }

        public Uri BaseAddress { get; }

        // TLS with a generated server certificate; client certificates are accepted.
        public Uri HttpsAddress { get; }

        // The host's process (its workers are its children).
        public Process Process => _process;

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static byte[] ServerCertificate(string password)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return certificate.Export(X509ContentType.Pfx, password);
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
            WebForCoreHost.DeleteTempDirectory(_tempDirectory);
            try { File.Delete(_certificateFile); } catch (IOException) { }
        }
    }

    [CollectionDefinition(Name)]
    public class IISSampleCollection : ICollectionFixture<SampleServer>
    {
        public const string Name = "IIS sample";
    }
}
