using Xunit;

namespace IISGate
{
    // <system.net><defaultProxy> in Web.config, which .NET's System.Net ignores and webForCore applies to
    // HttpClient.DefaultProxy when the application starts.
    [Collection(IISSampleCollection.Name)]
    public class NetTests
    {
        private readonly SampleServer _server;

        public NetTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task DefaultProxyFromConfiguration()
        {
            using var proxy = new FakeHttpProxy(31280);
            string body = await _server.Client.GetStringAsync("/net/Proxy.aspx");

            Assert.Contains("proxy=http://127.0.0.1:31280/;", body);
            Assert.Contains("bypassed=True;", body);       // <bypasslist>
            Assert.Contains("local=True;", body);          // bypassonlocal
            Assert.Contains("httpclient=proxied:/httpclient;", body);
            Assert.Contains("webclient=proxied:/webclient;", body);
            Assert.Contains("GET http://gate.example.invalid/httpclient HTTP/1.1", proxy.RequestLines);
        }
    }
}
