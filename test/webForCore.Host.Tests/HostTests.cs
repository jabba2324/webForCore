using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using WebForCoreGate;
using Xunit;

namespace HostTests
{
    // webforcore-host (#56) with test/webForCore.Host.Sample, and on its own. One class: the tests share the sample's
    // folder (its compilation output), so they run one at a time.
    public class HostTests
    {
        const string Sample = "webForCore.Host.Sample";
        static readonly string Repository = WebForCoreHost.Repository(Path.Combine("test", Sample));
        static readonly string SampleDirectory = Path.Combine(Repository, "test", Sample);

        static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        sealed class Running : IDisposable
        {
            readonly StringBuilder output = new StringBuilder();
            public Process Process { get; }
            public Uri Address { get; }
            public HttpClient Client { get; }

            readonly string tempDirectory;

            public Running(ProcessStartInfo start, Uri address, string tempDirectory = null)
            {
                this.tempDirectory = tempDirectory;
                Address = address;
                Process = Process.Start(start);
                Process.OutputDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
                Process.ErrorDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
                Process.BeginOutputReadLine();
                Process.BeginErrorReadLine();
                Client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(120) };
            }

            public string Output { get { lock (output) return output.ToString(); } }

            // The first answer that isn't a refused connection (the host or the application is up).
            public async Task<HttpResponseMessage> GetAsync(string path)
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (true)
                {
                    try { return await Client.GetAsync(path); }
                    catch (HttpRequestException) when (DateTime.UtcNow < deadline && !Process.HasExited) { await Task.Delay(250); }
                }
            }

            public async Task WaitForOutput(string text, TimeSpan within)
            {
                var deadline = DateTime.UtcNow + within;
                while (!Output.Contains(text) && DateTime.UtcNow < deadline) await Task.Delay(200);
                Assert.Contains(text, Output);
            }

            public void Dispose()
            {
                Client.Dispose();
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(10000);
                }
                Process.Dispose();
                if (tempDirectory != null) WebForCoreHost.DeleteTempDirectory(tempDirectory);
            }
        }

        // IIS's rapid-fail protection: after this many workers fail within the interval, no more are started, and
        // requests get 503.
        [Fact]
        public async Task RapidFailProtectionStopsStartingWorkers()
        {
            var folder = Directory.CreateTempSubdirectory("webforcore-rapidfail-");
            try
            {
                // Not an application: each worker exits at once.
                File.WriteAllText(Path.Combine(folder.FullName, "Broken.dll"), "not an assembly");
                var address = new Uri($"http://127.0.0.1:{FreePort()}");
                var start = WebForCoreHost.StartInfo(Repository, folder.FullName, "Broken", new Dictionary<string, string>
                {
                    ["Kestrel__Endpoints__Http__Url"] = address.ToString(),
                    ["WebForCore__Host__RapidFailProtectionMaxCrashes"] = "3",
                    ["WebForCore__Host__QueueTimeout"] = "00:00:30",
                }, out var temp);
                start.ArgumentList[2] = "Broken.dll";
                using var host = new Running(start, address, temp);
                var response = await host.GetAsync("/");
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await host.WaitForOutput("Rapid-fail protection: 3 worker failures", TimeSpan.FromSeconds(30));
                Assert.Contains("Started worker 3 ", host.Output);
                Assert.DoesNotContain("Started worker 4 ", host.Output);
                // Later requests too, without starting workers.
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/")).StatusCode);
                Assert.DoesNotContain("Started worker 4 ", host.Output);
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }

        // A worker never outlives its host: when the host ends (here, killed), the worker's stdin closes and it stops.
        [Fact]
        public async Task WorkersStopWhenTheHostEnds()
        {
            var address = new Uri($"http://127.0.0.1:{FreePort()}");
            var start = WebForCoreHost.StartInfo(Repository, SampleDirectory, Sample, new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = address.ToString(),
            }, out var temp);
            using var host = new Running(start, address, temp);
            var response = await host.GetAsync("/Default.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var match = Regex.Match(await response.Content.ReadAsStringAsync(), @"process=(\d+);");
            Assert.True(match.Success);
            using var worker = Process.GetProcessById(int.Parse(match.Groups[1].Value));

            host.Process.Kill(entireProcessTree: false);
            Assert.True(worker.WaitForExit(30000), "the worker was still running 30 s after its host ended");
        }

        // Without webforcore-host (dotnet run), the application serves its own endpoints, and a restart stops the process
        // (for whatever started it to start it again).
        [Fact]
        public async Task AnApplicationRunOnItsOwnStopsWhenItRestarts()
        {
            var address = new Uri($"http://127.0.0.1:{FreePort()}");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add($"bin_dotnet/{Sample}.dll");
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add(address.ToString());
            using var app = new Running(start, address);
            var response = await app.GetAsync("/Default.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"process={app.Process.Id};", await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync("/Restart.aspx")).StatusCode);
            Assert.True(app.Process.WaitForExit(60000), "the application was still running 60 s after it restarted");
            Assert.Contains("restarted outside webforcore-host: stopping the process", app.Output);
        }

        // A process without a locale has the invariant culture, which IIS never has: the application warns when it starts,
        // unless <globalization> gives requests a culture (the sample's Web.config doesn't) (#97).
        [NotOnWindowsTheory("the culture comes from the user's locale, not LC_ALL")]
        [InlineData("C", true)]
        [InlineData("en_US.UTF-8", false)]
        public async Task AnApplicationWithoutALocaleWarnsOfTheInvariantCulture(string locale, bool warns)
        {
            var address = new Uri($"http://127.0.0.1:{FreePort()}");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = SampleDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add($"bin_dotnet/{Sample}.dll");
            start.ArgumentList.Add("--urls");
            start.ArgumentList.Add(address.ToString());
            start.Environment["LC_ALL"] = locale;
            using var app = new Running(start, address);
            Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/Default.aspx")).StatusCode);
            const string warning = "runs with the invariant culture";
            if (warns) await app.WaitForOutput(warning, TimeSpan.FromSeconds(30));
            else
            {
                await app.WaitForOutput("Started application", TimeSpan.FromSeconds(30));
                await Task.Delay(1000);
                Assert.DoesNotContain(warning, app.Output);
            }
        }
    }

    // A theory that is skipped on Windows, with the reason, where the code under test takes Windows' own path.
    public sealed class NotOnWindowsTheoryAttribute : TheoryAttribute
    {
        public NotOnWindowsTheoryAttribute(string reason)
        {
            if (OperatingSystem.IsWindows()) Skip = "Windows: " + reason;
        }
    }
}
