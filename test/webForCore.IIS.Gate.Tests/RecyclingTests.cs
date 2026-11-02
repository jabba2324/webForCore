using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace IISGate
{
    // Process recycling (#56): the sample runs under webforcore-host. When the application restarts
    // (HttpRuntime.UnloadAppDomain, a Web.config change), the worker asks the host for its replacement and exits once it
    // has drained; the next worker runs the application with new static state. Requests are answered throughout, as IIS
    // answered them during a recycle.
    [Collection(IISSampleCollection.Name)]
    public class RecyclingTests
    {
        private readonly SampleServer _server;

        public RecyclingTests(SampleServer server)
        {
            _server = server;
        }

        private record Start(string Domain, int Starts, int Process);

        private async Task<Start> Current()
        {
            var response = await _server.Client.GetAsync("/Domain.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var match = Regex.Match(await response.Content.ReadAsStringAsync(), @"domain=([0-9a-f-]+);starts=(\d+);process=(\d+)");
            Assert.True(match.Success);
            return new Start(match.Groups[1].Value, int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        }

        // The application after `before` has been replaced.
        private async Task<Start> Next(Start before, TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            var current = before;
            while (current.Process == before.Process && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                current = await Current();
            }
            Assert.NotEqual(before.Process, current.Process);
            return current;
        }

        private async Task WaitForOutput(string text, TimeSpan within)
        {
            var deadline = DateTime.UtcNow + within;
            while (!_server.Output.Contains(text) && DateTime.UtcNow < deadline) await Task.Delay(200);
            Assert.Contains(text, _server.Output);
        }

        // HttpRuntime.UnloadAppDomain: a new worker process runs the application (Application_Start once, new static
        // state), and the old one exits by itself.
        [Fact]
        public async Task UnloadAppDomainRestartsTheApplicationInANewWorker()
        {
            var before = await Current();
            Assert.Equal(before, await Current());
            Assert.Equal(1, before.Starts); // Application_Start once per worker

            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Restart.aspx")).StatusCode);
            var after = await Next(before, TimeSpan.FromSeconds(60));
            Assert.NotEqual(before.Domain, after.Domain);
            Assert.Equal(1, after.Starts); // the new worker's own static state

            await WaitForOutput($"(process {before.Process}) exited (0).", TimeSpan.FromSeconds(60));
            Assert.Matches($@"Recycling worker \d+ \(process {before.Process}\): UnloadAppDomainCalled\.", _server.Output);
        }

        // Every request is answered while the application restarts: those that arrive while the next worker starts
        // wait for it (the host sends a request the old worker didn't take to the next one).
        [Fact]
        public async Task RequestsDuringARestartAreAllAnswered()
        {
            var before = await Current();
            using var stop = new CancellationTokenSource();
            var statuses = new System.Collections.Concurrent.ConcurrentBag<HttpStatusCode>();
            var load = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    statuses.Add((await _server.Client.GetAsync("/Domain.aspx")).StatusCode);
                }
            })).ToArray();
            await Task.Delay(500);
            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Restart.aspx")).StatusCode);
            await Next(before, TimeSpan.FromSeconds(60));
            await Task.Delay(500);
            stop.Cancel();
            await Task.WhenAll(load);
            Assert.True(statuses.Count > 10, $"{statuses.Count} requests");
            Assert.All(statuses, status => Assert.Equal(HttpStatusCode.OK, status));
        }

        // A WebSocket open across a restart: the old worker keeps serving it for <hostingEnvironment shutdownTimeout> (5 s
        // in the sample), then the connection is aborted, as AppDomain.Unload did on IIS, and the worker exits.
        [Fact]
        public async Task RestartAbortsAWebSocketAfterTheShutdownTimeout()
        {
            var before = await Current();

            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new UriBuilder(_server.BaseAddress) { Scheme = "ws", Path = "/Echo.ashx" }.Uri, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Restart.aspx")).StatusCode);

            var buffer = new byte[1024];
            var restarted = DateTime.UtcNow;
            bool aborted = false;
            while (!aborted && DateTime.UtcNow - restarted < TimeSpan.FromSeconds(30))
            {
                try
                {
                    await socket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    aborted = result.MessageType == WebSocketMessageType.Close;
                }
                catch (WebSocketException)
                {
                    aborted = true;
                }
                if (!aborted) await Task.Delay(500);
            }
            var elapsed = DateTime.UtcNow - restarted;
            Assert.True(aborted, "the WebSocket stayed open 30 s after the restart");
            Assert.InRange(elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(20)); // served until the timeout, not beyond

            await WaitForOutput($"(process {before.Process}) exited", TimeSpan.FromSeconds(30));
        }

        // Web.config changing again and again, also while the next worker starts (#49: in-process, a change while the
        // next start was starting left the application answering 503 for good): it ends with a worker serving.
        [Fact]
        public async Task ChangesWhileTheNextWorkerStartsEndInAServingWorker()
        {
            var before = await Current();
            var config = Path.Combine(_server.SampleDirectory, "Web.config");
            for (int i = 0; i < 30; i++)
            {
                File.SetLastWriteTimeUtc(config, DateTime.UtcNow);
                await Task.Delay(100);
            }
            var deadline = DateTime.UtcNow.AddSeconds(90);
            Start after = null;
            while (DateTime.UtcNow < deadline)
            {
                var response = await _server.Client.GetAsync("/Domain.aspx");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    after = await Current();
                    if (after.Process != before.Process) break;
                }
                await Task.Delay(250);
            }
            Assert.NotNull(after);
            Assert.NotEqual(before.Process, after.Process);
            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Domain.aspx")).StatusCode);
        }

        // A worker that exits by itself (a crash) is replaced, and requests are answered again.
        [Fact]
        public async Task AWorkerThatExitsIsReplaced()
        {
            var before = await Current();
            Assert.Equal("exiting 42", (await _server.Client.GetStringAsync("/Exit.aspx?code=42")).Trim());
            var after = await Next(before, TimeSpan.FromSeconds(60));
            Assert.Equal(1, after.Starts);
            Assert.Matches($@"Worker \d+ \(process {before.Process}\) exited unexpectedly \(42\)", _server.Output);
        }

        // #51: a change in bin or App_Code restarts the application, as on IIS, on every platform. On Linux only Web.config
        // did: a deployment that replaced the application's DLLs kept running the old code (found running the smoke
        // test's applications on Linux: MvcMovie's bin_dotnet, MiniBlog's App_Code).
        [Theory]
        [InlineData("bin_dotnet", "restart.gate")]
        [InlineData("App_Code", "DomainState.cs")]
        public async Task BinAndAppCodeChangesRestartTheApplication(string folder, string file)
        {
            var before = await Current();
            var path = Path.Combine(_server.SampleDirectory, folder, file);
            if (file.EndsWith(".cs")) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            else File.WriteAllText(path, DateTime.UtcNow.ToString("O"));
            var after = await Next(before, TimeSpan.FromSeconds(30));
            if (!file.EndsWith(".cs"))
            {
                // Removing it is a change in bin too: the next test starts after that restart.
                File.Delete(path);
                await Next(after, TimeSpan.FromSeconds(30));
            }
        }
    }
}
