using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace IISGate
{
    // The application runs in a PseudoAppDomain: HttpRuntime.UnloadAppDomain (as a web.config or bin change) restarts it
    // in-process with new static state, while requests keep being served, and the old domain starts unloading.
    [Collection(IISSampleCollection.Name)]
    public class AppDomainTests
    {
        private readonly SampleServer _server;

        public AppDomainTests(SampleServer server)
        {
            _server = server;
        }

        private async Task<(string Domain, int Starts)> Domain()
        {
            var response = await _server.Client.GetAsync("/Domain.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var match = Regex.Match(await response.Content.ReadAsStringAsync(), @"domain=([0-9a-f-]+);starts=(\d+)");
            Assert.True(match.Success);
            return (match.Groups[1].Value, int.Parse(match.Groups[2].Value));
        }

        [Fact]
        public async Task UnloadAppDomainRestartsInProcess()
        {
            var (before, starts) = await Domain();
            Assert.Equal(before, (await Domain()).Domain);
            Assert.Equal(1, starts); // Application_Start once per domain

            var restart = await _server.Client.GetAsync("/Restart.aspx");
            Assert.Equal(HttpStatusCode.OK, restart.StatusCode);

            string after = before;
            int afterStarts = 0;
            for (int i = 0; i < 100 && after == before; i++)
            {
                await Task.Delay(100);
                (after, afterStarts) = await Domain(); // every request is answered while the old domain shuts down
            }
            Assert.NotEqual(before, after);
            Assert.Equal(1, afterStarts); // the new domain's own static state

            // The old domain shut down, started unloading and is collected: nothing outside it references it any more
            // (the sample checks every 2 seconds; GATE_RECYCLE_CHECK).
            for (int i = 0; i < 300 && !_server.Output.Contains("(start 1) unloaded."); i++)
            {
                await Task.Delay(200);
            }
            Assert.Contains("(start 1) is unloading.", _server.Output);
            Assert.Contains("(start 1) unloaded.", _server.Output);
            Assert.DoesNotContain("has not unloaded", _server.Output);
        }
    }
}
