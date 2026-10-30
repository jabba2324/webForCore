using System.Diagnostics;
using System.Net;
using Xunit;

namespace IISGate
{
    // Protected configuration on Linux: RsaProtectedConfigurationProvider with a key container file and
    // DataProtectionConfigurationProvider with the DPAPI emulation, read by the app at run time; a section encrypted by
    // Windows DPAPI; webforcore-regiis (aspnet_regiis's -pef/-pdf/-pc/-px/-pi).
    [Collection(IISSampleCollection.Name)]
    public class ProtectedConfigurationTests
    {
        private readonly SampleServer _server;

        public ProtectedConfigurationTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task AppReadsProtectedSections()
        {
            var body = await _server.Client.GetStringAsync("/protected/Secrets.aspx");
            Assert.Contains("main=Server=db;Password=rsa-secret;", body);
            Assert.Contains("apikey=dpapi-secret;", body);
            Assert.Contains("rsa=True;", body);
            var key = await _server.Client.GetAsync("/App_Data/keys/NetFrameworkConfigurationKey.rsakey.xml");
            Assert.Equal(HttpStatusCode.NotFound, key.StatusCode); // App_Data is a hidden segment
        }

        [Fact]
        public async Task WindowsDpapiSectionExplainsItself()
        {
            var body = await _server.Client.GetStringAsync("/protected-windows/Read.aspx");
            Assert.Contains("error=", body);
            Assert.Contains("Windows DPAPI", body);
        }

        private static string Repository()
        {
            var dir = AppContext.BaseDirectory;
            while (!File.Exists(Path.Combine(dir, "WebFormsForCore.slnx"))) dir = Path.GetDirectoryName(dir);
            return dir;
        }

        private static (int Code, string Output) RegIIS(string keys, params string[] args)
        {
            var tool = Path.Combine(Repository(), "src", "WebFormsForCore.RegIIS", "bin", "Release", "net11.0", "webforcore-regiis.dll");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(tool);
            foreach (var a in args) start.ArgumentList.Add(a);
            start.Environment["WEBFORCORE_KEYS"] = keys;
            using var process = Process.Start(start);
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }

        // Protect with a new container, move the exported key to another key directory (another machine), unprotect.
        [Fact]
        public void RegIISProtectExportImportUnprotect()
        {
            var root = Path.Combine(Path.GetTempPath(), "webforcore-regiis-" + Guid.NewGuid().ToString("N"));
            var site = Path.Combine(root, "site");
            Directory.CreateDirectory(site);
            File.WriteAllText(Path.Combine(site, "web.config"),
                "<?xml version=\"1.0\"?><configuration><connectionStrings><add name=\"Db\" connectionString=\"secret-value\" /></connectionStrings></configuration>");
            string keysA = Path.Combine(root, "a"), keysB = Path.Combine(root, "b");
            try
            {
                Assert.Equal(0, RegIIS(keysA, "-pc", "NetFrameworkConfigurationKey", "-exp").Code);
                Assert.Equal(0, RegIIS(keysA, "-pef", "connectionStrings", site).Code);
                var encrypted = File.ReadAllText(Path.Combine(site, "web.config"));
                Assert.Contains("configProtectionProvider=\"RsaProtectedConfigurationProvider\"", encrypted);
                Assert.DoesNotContain("secret-value", encrypted);

                var (failCode, failOutput) = RegIIS(keysB, "-pdf", "connectionStrings", site); // no key there
                Assert.NotEqual(0, failCode);

                var export = Path.Combine(root, "key.xml");
                Assert.Equal(0, RegIIS(keysA, "-px", "NetFrameworkConfigurationKey", export, "-pri").Code);
                Assert.Equal(0, RegIIS(keysB, "-pi", "NetFrameworkConfigurationKey", export).Code);
                Assert.Equal(0, RegIIS(keysB, "-pdf", "connectionStrings", site).Code);
                Assert.Contains("connectionString=\"secret-value\"", File.ReadAllText(Path.Combine(site, "web.config")));

                // An imported non-exportable key keeps its private part.
                Assert.NotEqual(0, RegIIS(keysB, "-px", "NetFrameworkConfigurationKey", Path.Combine(root, "again.xml"), "-pri").Code);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
