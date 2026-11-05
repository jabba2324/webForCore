using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace HostTests
{
    // File change notifications with many folders, and <httpRuntime fcnMode> (#145, #146, #152). Part of HostTests, so
    // that the applications run one at a time: each holds inotify instances, of which a user has 128 by default.
    //
    // Each test runs a copy of test/webForCore.Host.Sample on its own (without webforcore-host, so a restart ends the
    // process), with the fcnMode given, and Monitor.aspx keeps a cache dependency on a file in each of 200 folders.
    public partial class HostTests
    {
        // What the application may hold, whatever the number of folders it monitors: ASP.NET Core's and webForCore's
        // configuration watchers (3), System.Web's three shared watchers of the site's folder, and the folders outside it
        // (the compilation folder, the site's parent), with room to spare. Before #145 each monitored folder took one
        // more (two for many), and the 200 folders failed at the user's limit of 128.
        const int InstanceBudget = 16;

        sealed class Site : IDisposable
        {
            public string Directory { get; }
            readonly string temp;

            public Site(string fcnMode)
            {
                temp = System.IO.Directory.CreateTempSubdirectory("webforcore-fcn-").FullName;
                Directory = Path.Combine(temp, "site");
                CopyDirectory(Path.Combine(SampleDirectory, "bin_dotnet"), Path.Combine(Directory, "bin_dotnet"));
                foreach (var file in new[] { "Default.aspx", "Monitor.aspx" })
                {
                    File.Copy(Path.Combine(SampleDirectory, file), Path.Combine(Directory, file));
                }
                var attribute = fcnMode == null ? "" : $" fcnMode=\"{fcnMode}\"";
                File.WriteAllText(Path.Combine(Directory, "Web.config"), $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <system.web>
                        <compilation debug="false" targetFramework="4.8" />
                        <httpRuntime targetFramework="4.8"{attribute} />
                      </system.web>
                    </configuration>
                    """);
                System.IO.Directory.CreateDirectory(Path.Combine(Directory, "App_Data", "unwatched"));
            }

            static void CopyDirectory(string from, string to)
            {
                System.IO.Directory.CreateDirectory(to);
                foreach (var file in System.IO.Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
                foreach (var dir in System.IO.Directory.GetDirectories(from)) CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
            }

            public Running Start()
            {
                var address = new Uri($"http://127.0.0.1:{FreePort()}");
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = Directory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add($"bin_dotnet/{Sample}.dll");
                start.ArgumentList.Add("--urls");
                start.ArgumentList.Add(address.ToString());
                // A compilation folder of its own.
                var tmp = Path.Combine(temp, "tmp");
                System.IO.Directory.CreateDirectory(tmp);
                foreach (var variable in new[] { "TMPDIR", "TEMP", "TMP" }) start.Environment[variable] = tmp;
                return new Running(start, address);
            }

            public void Dispose()
            {
                try { System.IO.Directory.Delete(temp, recursive: true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        static Dictionary<string, string> Values(string answer)
        {
            var values = new Dictionary<string, string>();
            foreach (Match match in Regex.Matches(answer, @"(\w+)=([^;]*);")) values[match.Groups[1].Value] = match.Groups[2].Value;
            return values;
        }

        static async Task<Dictionary<string, string>> Get(Running app, string query)
        {
            var response = await app.GetAsync("/Monitor.aspx?" + query);
            var answer = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {answer}\n{app.Output}");
            Assert.DoesNotContain("failed at folder", answer);
            return Values(answer);
        }

        // An application that monitors files in 200 folders (Orchard's modules, Kooboo's and nopCommerce's view folders)
        // holds no more inotify instances than one that monitors a few, in the default mode as with Single, and is told
        // of a change in any of them, a file moved between two of them included (#145, #152). The budget is Linux's:
        // elsewhere there is no such limit.
        [Theory]
        [InlineData(null)]
        [InlineData("Default")]
        [InlineData("Single")]
        public async Task ManyMonitoredFoldersStayWithinTheInotifyBudget(string fcnMode)
        {
            using var site = new Site(fcnMode);
            using var app = site.Start();
            var first = await Get(app, "watch=200");
            Assert.Equal("200", first["monitored"]);
            if (OperatingSystem.IsLinux())
            {
                Assert.True(int.Parse(first["instances"]) <= InstanceBudget, $"{first["instances"]} inotify instances for 200 folders");
            }

            var changed = await Get(app, "change=150");
            Assert.Equal("True", changed["cached"]);
            Assert.Equal("True", changed["changed"]);
            Assert.Equal(first["instances"], changed["instances"]);

            var moved = await Get(app, "move=1");
            Assert.Equal("True", moved["removed"]);
            Assert.Equal("True", moved["added"]);
        }

        // Web.config changes restart the application in every mode but Disabled, and so does renaming a folder that has
        // monitored files; deleting one that has none doesn't (Dev10 440497 in FileChangesMonitor).
        [Theory]
        [InlineData(null)]
        [InlineData("Default")]
        [InlineData("Single")]
        public async Task AChangeToWebConfigRestartsTheApplication(string fcnMode)
        {
            using var site = new Site(fcnMode);
            using var app = site.Start();
            Assert.Equal("200", (await Get(app, "watch=200"))["monitored"]);

            System.IO.Directory.Delete(Path.Combine(site.Directory, "App_Data", "unwatched"));
            await Task.Delay(2000);
            Assert.False(app.Process.HasExited, "deleting a folder without monitored files restarted the application\n" + app.Output);

            File.AppendAllText(Path.Combine(site.Directory, "Web.config"), "\n<!-- changed -->\n");
            Assert.True(app.Process.WaitForExit(30000), "the application was still running 30 s after Web.config changed\n" + app.Output);
            Assert.Contains("restarted outside webforcore-host: stopping the process", app.Output);
        }

        [Fact]
        public async Task RenamingAMonitoredFolderRestartsTheApplication()
        {
            using var site = new Site(null);
            using var app = site.Start();
            Assert.Equal("200", (await Get(app, "watch=200"))["monitored"]);

            var folders = Path.Combine(site.Directory, "App_Data", "folders");
            System.IO.Directory.Move(Path.Combine(folders, "f10"), Path.Combine(folders, "f10-renamed"));
            Assert.True(app.Process.WaitForExit(30000), "the application was still running 30 s after a monitored folder was renamed\n" + app.Output);
        }

        // fcnMode="Disabled" monitors nothing: no inotify instance for the folders, no notification of a change, and no
        // restart when Web.config changes, as on IIS (#146).
        [Fact]
        public async Task FcnModeDisabledMonitorsNothing()
        {
            using var site = new Site("Disabled");
            using var app = site.Start();
            var before = await Get(app, "");
            var watched = await Get(app, "watch=200");
            Assert.Equal("200", watched["monitored"]);
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal(before["instances"], watched["instances"]);
            }

            var changed = await Get(app, "change=150");
            Assert.Equal("True", changed["cached"]);
            Assert.Equal("False", changed["changed"]);

            File.AppendAllText(Path.Combine(site.Directory, "Web.config"), "\n<!-- changed -->\n");
            await Task.Delay(5000);
            Assert.False(app.Process.HasExited, "the application restarted when Web.config changed\n" + app.Output);
            Assert.Equal(before["process"], (await Get(app, ""))["process"]);
        }
    }
}
