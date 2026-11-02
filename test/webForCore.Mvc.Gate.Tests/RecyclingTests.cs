using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace MvcGate
{
    // Process recycling (#56): a web.config change restarts the application in a new worker process of webforcore-host;
    // the old worker drains and exits.
    [Collection(MvcSampleCollection.Name)]
    public class RecyclingTests
    {
        private readonly SampleServer _server;

        public RecyclingTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task AWebConfigChangeRestartsTheApplicationInANewWorker()
        {
            // Model binding, validation and metadata of the sample's types, views, an MVC Futures serializer.
            foreach (string path in new[] { "/Products", "/Products/Details/2", "/Products/Sum?values=1&values=2", "/Products/Edit/2", "/Futures/Wizard", "/Home/About" })
            {
                Assert.True((await _server.Client.GetAsync(path)).StatusCode == HttpStatusCode.OK, path);
            }
            int workers = Regex.Matches(_server.Output, @"Started worker \d+").Count;

            File.SetLastWriteTimeUtc(Path.Combine(_server.SampleDirectory, "Web.config"), DateTime.UtcNow);

            // The old worker is recycled and exits; the next one serves.
            var exited = new Regex(@"Recycling worker (\d+) \(process (\d+)\)");
            for (int i = 0; i < 300 && !(exited.Match(_server.Output) is { Success: true } m && _server.Output.Contains($"(process {m.Groups[2].Value}) exited (0).")); i++)
            {
                await Task.Delay(200);
            }
            var recycled = exited.Match(_server.Output);
            Assert.True(recycled.Success, "no worker was recycled");
            Assert.Contains($"(process {recycled.Groups[2].Value}) exited (0).", _server.Output);
            Assert.Equal(HttpStatusCode.OK, (await _server.Client.GetAsync("/Products/Details/2")).StatusCode);
            Assert.True(Regex.Matches(_server.Output, @"Started worker \d+").Count > workers);
        }
    }
}
