using System.Diagnostics;
using System.Net;
using WebForCoreGate;
using Xunit;

namespace HostTests
{
    // WebForCore:Host:SiteId (#210), the IIS site's ID in the application's ID, /LM/W3SVC/<SiteId>/ROOT. The session
    // state gate (SessionCache.Gate.Tests' SiteIdTests) tests what it changes; these, the setting itself.
    public partial class HostTests
    {
        // IIS's site IDs are 1 or more: the host doesn't start with another.
        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        public void TheHostRefusesASiteIdBelowOne(string siteId)
        {
            var start = WebForCoreHost.StartInfo(Repository, SampleDirectory, Sample, new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = $"http://127.0.0.1:{FreePort()}",
                ["WebForCore__Host__SiteId"] = siteId,
            }, out var temp);
            try
            {
                using var host = Process.Start(start);
                var error = host.StandardError.ReadToEndAsync();
                host.StandardOutput.ReadToEnd();
                Assert.True(host.WaitForExit(30000), "the host was still running");
                Assert.Equal(2, host.ExitCode);
                Assert.Contains($"WebForCore:Host:SiteId is {siteId}; an IIS site's ID is 1 or more.", error.Result);
            }
            finally
            {
                WebForCoreHost.DeleteTempDirectory(temp);
            }
        }

        // An application run on its own (without the host) takes the same key from its own configuration.
        [Fact]
        public async Task AnApplicationRunOnItsOwnTakesTheSiteIdFromItsConfiguration()
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
            start.ArgumentList.Add("--WebForCore:Host:SiteId=4");
            using var app = new Running(start, address);
            Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/Default.aspx")).StatusCode);
            await app.WaitForOutput("Started application /LM/W3SVC/4/ROOT in process", TimeSpan.FromSeconds(30));
        }
    }
}
