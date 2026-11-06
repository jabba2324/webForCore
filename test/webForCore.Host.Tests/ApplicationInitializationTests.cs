using System.Net;
using System.Text.RegularExpressions;
using WebForCoreGate;
using Xunit;

namespace HostTests
{
    // IIS's start mode, Application Initialization and idle timeout in webforcore-host (#193): startMode="AlwaysRunning"
    // starts the worker with the host; <applicationInitialization>'s pages are requested before a new worker takes
    // traffic, after the host starts and after a recycle (an application pool recycle overlapped, the old worker serving
    // meanwhile); a page that fails doesn't stop the worker from serving; idleTimeout stops an idle worker. Part of
    // HostTests, so that the applications run one at a time.
    //
    // Each test runs a copy of test/webForCore.Host.Sample under webforcore-host with the Web.config it needs.
    public partial class HostTests
    {
        const string WarmUpAgent = "IIS Application Initialization Warmup";

        sealed class InitSite : IDisposable
        {
            public string Directory { get; }
            readonly string temp;

            public InitSite(string webServer)
            {
                temp = System.IO.Directory.CreateTempSubdirectory("webforcore-appinit-").FullName;
                Directory = Path.Combine(temp, "site");
                Site.CopyDirectory(Path.Combine(SampleDirectory, "bin_dotnet"), Path.Combine(Directory, "bin_dotnet"));
                foreach (var file in new[] { "Default.aspx", "Restart.aspx", "Warm.aspx", "State.aspx", "Fail.aspx", "Startup.htm" })
                    File.Copy(Path.Combine(SampleDirectory, file), Path.Combine(Directory, file));
                File.WriteAllText(Path.Combine(Directory, "Web.config"), $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <system.web>
                        <compilation debug="false" targetFramework="4.8" />
                        <httpRuntime targetFramework="4.8" />
                        <hostingEnvironment shutdownTimeout="5" />
                      </system.web>
                      <system.webServer>
                        {webServer}
                      </system.webServer>
                    </configuration>
                    """);
            }

            public Running Start(Dictionary<string, string> settings = null)
            {
                var address = new Uri($"http://127.0.0.1:{FreePort()}");
                var environment = new Dictionary<string, string>(settings ?? new Dictionary<string, string>())
                {
                    ["Kestrel__Endpoints__Http__Url"] = address.ToString(),
                };
                var start = WebForCoreHost.StartInfo(Repository, Directory, Sample, environment, out var tempDirectory);
                return new Running(start, address, tempDirectory);
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(temp, recursive: true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        record State(int Process, string Warm);

        // An answer other than 200 fails with its error and, on Linux, the user's inotify instances first (#204: on a
        // machine shared with other test runs, their processes held 119 of the 128, and the worker answered 500).
        static async Task<State> GetState(Running host)
        {
            var response = await host.GetAsync("/State.aspx");
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, host.Unexpected("/State.aspx", response, body));
            var match = Regex.Match(body, @"process=(\d+);warm=([^;]*);");
            Assert.True(match.Success, body);
            return new State(int.Parse(match.Groups[1].Value), match.Groups[2].Value);
        }

        static string Warmed(string n, string host = "localhost") => $"{n}|{host}|{WarmUpAgent}|127.0.0.1,";

        // startMode="AlwaysRunning", the host's default: the worker starts with the host, before any request.
        [Fact]
        public async Task AlwaysRunningStartsTheWorkerWithTheHost()
        {
            using var site = new InitSite("");
            using var host = site.Start();
            await host.WaitForOutput("is serving", TimeSpan.FromSeconds(90));
            Assert.Contains("Started worker 1 ", host.Output);
            Assert.Contains("the host started", host.Output);
        }

        // startMode="OnDemand": no worker until the first request, which starts one and is answered by it.
        [Fact]
        public async Task OnDemandStartsTheWorkerAtTheFirstRequest()
        {
            using var site = new InitSite("");
            using var host = site.Start(new Dictionary<string, string> { ["WebForCore__Host__StartMode"] = "OnDemand" });
            await host.WaitForOutput("Now listening on", TimeSpan.FromSeconds(60));
            await Task.Delay(2000);
            Assert.DoesNotContain("Started worker", host.Output);
            var state = await GetState(host);
            Assert.Contains("Started worker 1 ", host.Output);
            Assert.Contains("a request arrived", host.Output);
            Assert.Equal("", state.Warm);
        }

        // <applicationInitialization>: the pages, in order, with their hostName and IIS's user agent, from the server
        // itself, before the first request is served; that request waits for them.
        [Fact]
        public async Task InitializationPagesAreRequestedBeforeTheFirstRequest()
        {
            using var site = new InitSite("""
                <applicationInitialization>
                  <add initializationPage="/Warm.aspx?n=1&amp;sleep=2000" />
                  <add initializationPage="Warm.aspx?n=2" hostName="warm.example" />
                </applicationInitialization>
                """);
            using var host = site.Start();
            var state = await GetState(host);
            Assert.Equal(Warmed("1") + Warmed("2", "warm.example"), state.Warm);
            Assert.Contains("is warm", host.Output);
        }

        // remapManagedRequestsTo: while the worker warms up, requests get the file; then the application.
        [Fact]
        public async Task RemapManagedRequestsToAnswersWhileTheWorkerWarmsUp()
        {
            using var site = new InitSite("""
                <applicationInitialization remapManagedRequestsTo="Startup.htm">
                  <add initializationPage="/Warm.aspx?n=1&amp;sleep=8000" />
                </applicationInitialization>
                """);
            using var host = site.Start();
            var first = await host.GetAsync("/State.aspx");
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Contains("The application is starting.", await first.Content.ReadAsStringAsync());
            Assert.Equal("text/html", first.Content.Headers.ContentType?.MediaType);

            await host.WaitForOutput("is warm", TimeSpan.FromSeconds(90));
            var state = await GetState(host);
            Assert.Equal(Warmed("1"), state.Warm);
        }

        // doAppInitAfterRestart: the worker that replaces one whose application restarted is warmed before it serves
        // only with it, as on IIS; the requests meanwhile wait for it.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DoAppInitAfterRestartWarmsTheNextWorker(bool afterRestart)
        {
            using var site = new InitSite($"""
                <applicationInitialization doAppInitAfterRestart="{(afterRestart ? "true" : "false")}">
                  <add initializationPage="/Warm.aspx?n=1&amp;sleep=2000" />
                </applicationInitialization>
                """);
            using var host = site.Start();
            var before = await GetState(host);
            Assert.Equal(Warmed("1"), before.Warm);

            Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/Restart.aspx")).StatusCode);
            var after = before;
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (after.Process == before.Process && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                after = await GetState(host);
            }
            Assert.NotEqual(before.Process, after.Process);
            // The first answer of the new worker.
            Assert.Equal(afterRestart ? Warmed("1") : "", after.Warm);
        }

        // An application pool recycle (Recycling(...), here after 6 requests) is overlapped: the old worker serves while
        // the new one starts and is warmed; then the new one serves, already warm, and the old one exits.
        [Fact]
        public async Task AnApplicationPoolRecycleWarmsTheNextWorkerWhileTheOldOneServes()
        {
            using var site = new InitSite("""
                <applicationInitialization>
                  <add initializationPage="/Warm.aspx?n=1&amp;sleep=4000" />
                </applicationInitialization>
                """);
            using var host = site.Start(new Dictionary<string, string> { ["HOST_SAMPLE_RECYCLE_REQUESTS"] = "6" });
            var first = await GetState(host);
            Assert.Equal(Warmed("1"), first.Warm);

            var answers = new List<(State State, bool Overlapping)>();
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline)
            {
                bool overlapping = host.Output.Contains("It serves until its replacement is ready") && !Regex.IsMatch(host.Output, @"Worker 2 \(process \d+\) is warm");
                var state = await GetState(host);
                answers.Add((state, overlapping));
                if (answers.Count(a => a.State.Process != first.Process) >= 3) break;
                await Task.Delay(100);
            }
            var fromOld = answers.TakeWhile(a => a.State.Process == first.Process).ToList();
            var fromNew = answers.Skip(fromOld.Count).ToList();
            Assert.True(fromNew.Count >= 3, host.Explain("the next worker didn't serve"));
            // While the next worker warmed up, the old one served (each request takes a moment; warm-up takes 4 s).
            Assert.True(fromOld.Count(a => a.Overlapping) >= 3, host.Explain($"{fromOld.Count(a => a.Overlapping)} answers from the old worker during the warm-up"));
            // Once the next worker serves, the old one doesn't, and every answer of the next one comes after its warm-up.
            Assert.All(fromNew, a => Assert.NotEqual(first.Process, a.State.Process));
            Assert.All(fromNew, a => Assert.Equal(Warmed("1"), a.State.Warm));
            await host.WaitForOutput($"(process {first.Process}) exited (0).", TimeSpan.FromSeconds(60));
            Assert.DoesNotContain("exited unexpectedly", host.Output);
        }

        // A page that fails (500, 404) is logged, and the worker serves after it, as on IIS; the pages come from the
        // host's settings here, in place of Web.config's.
        [Fact]
        public async Task AFailingInitializationPageDoesNotStopTheWorker()
        {
            using var site = new InitSite("""
                <applicationInitialization>
                  <add initializationPage="/Warm.aspx?n=web.config" />
                </applicationInitialization>
                """);
            using var host = site.Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__InitializationPages__0__InitializationPage"] = "/Fail.aspx",
                ["WebForCore__Host__InitializationPages__1__InitializationPage"] = "/Missing.aspx",
                ["WebForCore__Host__InitializationPages__2__InitializationPage"] = "/Warm.aspx?n=3",
            });
            var state = await GetState(host);
            Assert.Equal(Warmed("3"), state.Warm);
            Assert.Contains("initialization page /Fail.aspx answered 500", host.Output);
            Assert.Contains("initialization page /Missing.aspx answered 404", host.Output);
            Assert.DoesNotContain("exited unexpectedly", host.Output);
        }

        // idleTimeout: a worker without requests for that long stops (its application ends); the next request starts a
        // new one (OnDemand), or one starts at once (AlwaysRunning, as WAS keeps such a pool's worker running).
        [Theory]
        [InlineData("OnDemand")]
        [InlineData("AlwaysRunning")]
        public async Task TheIdleTimeoutStopsTheWorker(string startMode)
        {
            using var site = new InitSite("");
            using var host = site.Start(new Dictionary<string, string>
            {
                ["WebForCore__Host__StartMode"] = startMode,
                ["WebForCore__Host__IdleTimeout"] = "00:00:03",
            });
            var before = await GetState(host);
            await host.WaitForOutput("was idle for 00:00:03: stopping it.", TimeSpan.FromSeconds(30));
            await host.WaitForOutput($"(process {before.Process}) exited", TimeSpan.FromSeconds(60));
            if (startMode == "AlwaysRunning")
                await host.WaitForOutput("Started worker 2 ", TimeSpan.FromSeconds(10));
            else
            {
                await Task.Delay(1000);
                Assert.DoesNotContain("Started worker 2 ", host.Output);
            }
            var after = await GetState(host);
            Assert.NotEqual(before.Process, after.Process);
            Assert.Contains("Started worker 2 ", host.Output);
            Assert.DoesNotContain("exited unexpectedly", host.Output);
        }
    }
}
