using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace IISGate
{
    // The worker request's side of IIS: WebSockets (AcceptWebSocketRequest), HTTPS and client certificates (server
    // variables, Request.ClientCertificate), executionTimeout, and bufferless reads of the request body.
    [Collection(IISSampleCollection.Name)]
    public class ConnectionTests
    {
        private readonly SampleServer _server;

        public ConnectionTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task WebSocketEcho()
        {
            using var socket = new ClientWebSocket();
            socket.Options.AddSubProtocol("gate.v1");
            var uri = new UriBuilder(_server.BaseAddress) { Scheme = "ws", Path = "/Echo.ashx", Query = "tag=t1" }.Uri;
            await socket.ConnectAsync(uri, CancellationToken.None);
            Assert.Equal("gate.v1", socket.SubProtocol);
            foreach (var message in new[] { "hello", "again" })
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                var buffer = new byte[1024];
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                Assert.Equal($"echo(t1,/Echo.ashx): {message}", Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
        }

        [Fact]
        public async Task WebSocketHandlerWithoutUpgrade()
        {
            var body = await _server.Client.GetStringAsync("/Echo.ashx");
            Assert.Equal("not a websocket request", body);
        }

        [Fact]
        public async Task HttpServerVariables()
        {
            var body = await _server.Client.GetStringAsync("/Connection.aspx");
            Assert.Contains("https=off;port_secure=0;secure=False;scheme=http;", body);
            Assert.Contains("instance=1;cert_flags=0;present=False;", body);
            Assert.Contains("connected=True;", body);
        }

        [Fact]
        public async Task HttpsAndClientCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Gate Client, O=webForCore, C=GB", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                ClientCertificateOptions = ClientCertificateOption.Manual,
            };
            handler.ClientCertificates.Add(certificate);
            using var client = new HttpClient(handler) { BaseAddress = _server.HttpsAddress };
            var body = await client.GetStringAsync("/Connection.aspx");
            Assert.Contains("https=on;port_secure=1;secure=True;scheme=https;", body);
            Assert.Contains("cert_flags=3;present=True;", body); // present, issuer not trusted (self-signed)
            Assert.Contains("cert_subject=C=GB, O=webForCore, CN=Gate Client;", body);
            Assert.Contains("subject=C=GB, O=webForCore, CN=Gate Client;", body);
            Assert.Contains($"bytes={certificate.RawData.Length};", body);
            var serial = string.Join("-", Enumerable.Range(0, certificate.SerialNumber.Length / 2).Select(i => certificate.SerialNumber.Substring(i * 2, 2).ToLowerInvariant()));
            Assert.Contains($"serial={serial};", body);
        }

        // ASP.NET answers 500 "Request timed out." after executionTimeout (its timer checks every 15 seconds); the page's
        // code is not aborted (.NET cannot) but sees Request.TimedOutToken.
        [Fact]
        public async Task ExecutionTimeout()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _server.Client.GetAsync("/slow/Wait.aspx");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains("Request timed out.", await response.Content.ReadAsStringAsync());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(40), watch.Elapsed.ToString());
        }

        [Fact]
        public async Task BufferlessRead()
        {
            var data = new byte[200000];
            new Random(5).NextBytes(data);
            long sum = 0;
            foreach (var b in data) sum = (sum * 31 + b) % 1000000007;
            var response = await _server.Client.PostAsync("/Bufferless.aspx", new ByteArrayContent(data));
            Assert.Equal($"length={data.Length};sum={sum}", (await response.Content.ReadAsStringAsync()).Trim());
        }
    }
}
