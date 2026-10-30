using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Transports;
using Xunit;

namespace IISGate
{
    // SignalR 2 (lifted) on Katana's System.Web host, on the fork's System.Web on Kestrel, with SignalR's .NET client:
    // each transport the client has (WebSockets through AcceptWebSocketRequest, Server-Sent Events, long polling).
    [Collection(IISSampleCollection.Name)]
    public class SignalRTests
    {
        private readonly SampleServer _server;

        public SignalRTests(SampleServer server)
        {
            _server = server;
        }

        public static IEnumerable<object[]> Transports => new[]
        {
            new object[] { "webSockets" }, new object[] { "serverSentEvents" }, new object[] { "longPolling" },
        };

        private static IClientTransport Transport(string name) => name switch
        {
            "webSockets" => new WebSocketTransport(),
            "serverSentEvents" => new ServerSentEventsTransport(),
            _ => new LongPollingTransport(),
        };

        [Theory]
        [MemberData(nameof(Transports))]
        public async Task HubOverTransport(string transport)
        {
            using var connection = new HubConnection(_server.BaseAddress.ToString());
            var hub = connection.CreateHubProxy("GateHub");
            var pushed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<string>("echoed", message => pushed.TrySetResult(message));

            await connection.Start(Transport(transport)).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(transport, connection.Transport.Name);
            Assert.Equal(ConnectionState.Connected, connection.State);

            var result = await hub.Invoke<string>("Echo", "hello " + transport).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal($"hub: hello {transport} (/signalr/send)", result.Replace("/signalr/connect", "/signalr/send").Replace("/signalr/poll", "/signalr/send"));
            Assert.Equal("pushed: hello " + transport, await pushed.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            connection.Stop();
        }

        [Fact]
        public async Task HubsProxyScript()
        {
            var script = await _server.Client.GetStringAsync("/signalr/hubs");
            Assert.Contains("proxies['gateHub']", script);
        }
    }
}

namespace IISGate
{
    // System.Web.Optimization (the fork's port): bundles rendered with a version hash, served concatenated and minified.
    [Collection(IISSampleCollection.Name)]
    public class BundlingTests
    {
        private readonly SampleServer _server;

        public BundlingTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task ScriptAndStyleBundles()
        {
            var page = await _server.Client.GetStringAsync("/Bundles.aspx");
            var script = System.Text.RegularExpressions.Regex.Match(page, "src=\"(/bundles/gate\\?v=[^\"]+)\"");
            var style = System.Text.RegularExpressions.Regex.Match(page, "href=\"(/Content/gatecss\\?v=[^\"]+)\"");
            Assert.True(script.Success, page);
            Assert.True(style.Success, page);

            var js = await _server.Client.GetAsync(script.Groups[1].Value);
            Assert.Equal("text/javascript", js.Content.Headers.ContentType.MediaType);
            var jsBody = await js.Content.ReadAsStringAsync();
            Assert.Contains("function gateOne(", jsBody);
            Assert.Contains("function gateTwo()", jsBody);
            Assert.DoesNotContain("longParameterName", jsBody); // minified: parameters renamed
            Assert.DoesNotContain("a comment", jsBody);

            var css = await (await _server.Client.GetAsync(style.Groups[1].Value)).Content.ReadAsStringAsync();
            Assert.Equal("body{color:red}", css.Trim());
        }
    }
}
