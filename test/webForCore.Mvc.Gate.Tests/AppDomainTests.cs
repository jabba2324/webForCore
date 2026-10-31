using System.Net;
using Xunit;

namespace MvcGate
{
    // A web.config change restarts the application in a new PseudoAppDomain, and the old one is collected: nothing outside
    // it (the shared framework's caches: model metadata, DataAnnotations, MemoryCache) keeps referencing it.
    [Collection(MvcSampleCollection.Name)]
    public class AppDomainTests
    {
        private readonly SampleServer _server;

        public AppDomainTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task OldDomainUnloadsAfterARestart()
        {
            // Model binding, validation and metadata of the sample's types, views, an MVC Futures serializer.
            foreach (string path in new[] { "/Products", "/Products/Details/2", "/Products/Sum?values=1&values=2", "/Products/Edit/2", "/Futures/Wizard", "/Home/About" })
            {
                Assert.True((await _server.Client.GetAsync(path)).StatusCode == HttpStatusCode.OK, path);
            }

            File.SetLastWriteTimeUtc(Path.Combine(_server.SampleDirectory, "Web.config"), DateTime.UtcNow);

            for (int i = 0; i < 300 && !_server.Output.Contains("(start 1) unloaded."); i++)
            {
                await Task.Delay(200);
                if (i % 10 == 0) await _server.Client.GetAsync("/"); // the next start
            }
            Assert.Contains("(start 1) is unloading.", _server.Output);
            Assert.Contains("(start 1) unloaded.", _server.Output);
            Assert.DoesNotContain("has not unloaded", _server.Output);
            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Products/Details/2")).StatusCode);
        }
    }
}
