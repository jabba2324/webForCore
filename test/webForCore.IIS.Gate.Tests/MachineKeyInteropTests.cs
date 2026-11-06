using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MachineKeyInterop;
using WebForCoreGate;
using Xunit;

namespace IISGate
{
    // #191: the same <machineKey> gives the same protected output on webForCore as on .NET Framework 4.8, in both
    // directions, for each configuration of MachineKeyConfigs (validation SHA1, HMACSHA256, HMACSHA512; decryption AES,
    // 3DES; compatibilityMode Framework20SP1, Framework20SP2, Framework45, none with <httpRuntime targetFramework="4.5">
    // and none at all; ticketCompatibilityMode Framework40; IsolateApps and IsolateByAppId).
    //
    // test/webForCore.MachineKey.Sample runs once for each configuration (a site with its Web.config). It reads what .NET
    // Framework 4.8 protected with that <machineKey> (FrameworkData/machinekey-framework.json, written by
    // tools/machinekey/FrameworkMachineKey on Windows): FormsAuthentication tickets, MachineKey.Protect (with and without
    // purposes) and MachineKey.Encode output, and Katana cookie tickets, a plain one and ASP.NET Identity's, which
    // Katana's cookie middleware reads through Microsoft.Owin.Host.SystemWeb's machine-key data protection. An output .NET
    // Framework gives the same every time (no random IV: Framework20SP1's tickets, Encode with Validation) must be the
    // same bytes on webForCore. webForCore's own outputs (the cookies as the middleware issued them) are written to
    // machinekey-webforcore/, which CI's machinekey-framework-interop job has .NET Framework 4.8 read.
    public class MachineKeyInteropTests
    {
        const string Sample = "webForCore.MachineKey.Sample";
        static readonly string Repository = WebForCoreHost.Repository(Path.Combine("test", Sample));
        static readonly string SampleDirectory = Path.Combine(Repository, "test", Sample);
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public static IEnumerable<object[]> Configs => MachineKeyConfigs.All.Select(c => new object[] { c.Name });

        static List<InteropEntry> FrameworkEntries()
        {
            string file = Path.Combine(AppContext.BaseDirectory, "FrameworkData", "machinekey-framework.json");
            return JsonSerializer.Deserialize<List<InteropEntry>>(File.ReadAllText(file));
        }

        [Fact]
        public void FrameworkDataCoversEveryConfiguration()
        {
            var framework = FrameworkEntries();
            Assert.Empty(MachineKeyConfigs.All.Where(c => !framework.Any(e => e.Config == c.Name)).Select(c => c.Name));
            Assert.Empty(framework.Where(e => MachineKeyConfigs.Find(e.Config) == null).Select(e => e.Config).Distinct());
        }

        [Theory]
        [MemberData(nameof(Configs))]
        public async Task OutputReadsOnTheOtherRuntime(string name)
        {
            MachineKeyConfig config = MachineKeyConfigs.Find(name);
            List<InteropEntry> framework = FrameworkEntries().Where(e => e.Config == name).ToList();

            using var app = new App(config);
            await app.Started();

            // webForCore's outputs: System.Web's from the handler, Katana's cookies from the cookie middleware.
            var mine = await app.Produce();
            foreach (string type in MachineKeyConfigs.OwinTypes)
            {
                string first = await app.SignIn(type), second = await app.SignIn(type);
                mine.Add(new InteropEntry { Config = name, Kind = "owin", Case = type, Value = first, Deterministic = first == second });
            }
            string outputs = Path.Combine(AppContext.BaseDirectory, "machinekey-webforcore");
            Directory.CreateDirectory(outputs);
            File.WriteAllText(Path.Combine(outputs, name + ".json"), JsonSerializer.Serialize(mine, Json));

            var problems = new List<string>();
            if (framework.Count == 0) problems.Add("no .NET Framework output for this configuration in FrameworkData/machinekey-framework.json");
            // .NET Framework's outputs read on webForCore (and the known differences still differ).
            foreach (var (entry, problem) in await app.Consume(framework))
            {
                string known = MachineKeyConfigs.KnownDifference(name, entry.Kind);
                if (problem != null && known == null) problems.Add($"{entry.Kind} {entry.Case}: .NET Framework's doesn't read: {problem}");
                if (problem == null && known != null) problems.Add($"{entry.Kind} {entry.Case}: .NET Framework's reads, but is listed as a known difference ({known})");
            }
            // webForCore's own read back.
            foreach (var (entry, problem) in await app.Consume(mine))
            {
                if (problem != null) problems.Add($"{entry.Kind} {entry.Case}: webForCore's own doesn't read: {problem}");
            }
            // Deterministic output is the same bytes.
            foreach (InteropEntry entry in framework)
            {
                InteropEntry same = mine.SingleOrDefault(e => e.Kind == entry.Kind && e.Case == entry.Case);
                if (same == null)
                {
                    problems.Add($"{entry.Kind} {entry.Case}: no webForCore output");
                    continue;
                }
                if (same.Deterministic != entry.Deterministic)
                    problems.Add($"{entry.Kind} {entry.Case}: deterministic on {(entry.Deterministic ? ".NET Framework" : "webForCore")} only");
                else if (entry.Deterministic && same.Value != entry.Value && MachineKeyConfigs.KnownDifference(name, entry.Kind) == null)
                    problems.Add($"{entry.Kind} {entry.Case}: webForCore wrote {same.Value}, .NET Framework {entry.Value}");
            }
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems) + Environment.NewLine + app.Output);
        }

        // The sample for one configuration: a site with its Web.config, run on its own on a free port.
        sealed class App : IDisposable
        {
            readonly Process process;
            readonly StringBuilder output = new StringBuilder();
            readonly string folder;
            readonly string site;
            readonly HttpClient client;
            readonly string root;

            public App(MachineKeyConfig config)
            {
                folder = Directory.CreateTempSubdirectory("webforcore-machinekey-").FullName;
                site = Directory.CreateDirectory(Path.Combine(folder, "site")).FullName;
                string temp = Directory.CreateDirectory(Path.Combine(folder, "temp")).FullName;
                File.WriteAllText(Path.Combine(site, "Web.config"), config.WebConfig());
                root = config.VirtualPath.TrimEnd('/') + "/";
                var start = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = SampleDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add($"bin_dotnet/{Sample}.dll");
                start.ArgumentList.Add("--urls");
                // Port 0: Kestrel takes a free port and says which ("Now listening on: ..."). A port picked here could be
                // taken by another suite's application before this one binds it, and the requests would go there.
                start.ArgumentList.Add("http://127.0.0.1:0");
                start.Environment["MACHINEKEY_SITE"] = site;
                start.Environment["MACHINEKEY_VIRTUAL_PATH"] = config.VirtualPath;
                // Temporary ASP.NET Files beside the site (a change in it restarts the application), deleted with it.
                foreach (var variable in new[] { "TMPDIR", "TEMP", "TMP" }) start.Environment[variable] = temp;
                process = Process.Start(start);
                process.OutputDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
                {
                    Timeout = TimeSpan.FromSeconds(120),
                };
            }

            public string Output { get { lock (output) return output.ToString(); } }

            public async Task Started()
            {
                var deadline = DateTime.UtcNow.AddSeconds(90);
                Match listening;
                while (!(listening = Regex.Match(Output, @"Now listening on: (http://\S+)")).Success)
                {
                    Assert.True(DateTime.UtcNow < deadline && !process.HasExited, "the application didn't start" + Environment.NewLine + Output);
                    await Task.Delay(100);
                }
                client.BaseAddress = new Uri(listening.Groups[1].Value);
                while (true)
                {
                    try
                    {
                        // 503 while the application starts.
                        if ((await client.GetAsync(root + "interop.axd")).StatusCode != HttpStatusCode.ServiceUnavailable || DateTime.UtcNow >= deadline) return;
                    }
                    catch (HttpRequestException) when (DateTime.UtcNow < deadline && !process.HasExited)
                    {
                    }
                    await Task.Delay(250);
                }
            }

            public async Task<List<InteropEntry>> Produce()
            {
                var response = await client.GetAsync(root + "interop.axd?op=produce");
                string body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {body}{Environment.NewLine}{Output}");
                return JsonSerializer.Deserialize<List<InteropEntry>>(body);
            }

            // Signs in with Katana's cookie middleware, and answers the cookie it issued, or "error: <exception type>".
            public async Task<string> SignIn(string type)
            {
                var response = await client.GetAsync(root + "owin/signin?type=" + type);
                string body = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.InternalServerError && body.StartsWith("error: ", StringComparison.Ordinal)) return body;
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {body}");
                string prefix = ".AspNet." + type + "=";
                string cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(prefix, StringComparison.Ordinal));
                return cookie.Substring(prefix.Length).Split(';')[0];
            }

            // Reads outputs: System.Web's in the handler, the cookies through the cookie middleware.
            public async Task<List<(InteropEntry, string)>> Consume(List<InteropEntry> entries)
            {
                var results = new List<(InteropEntry, string)>();
                var handled = entries.Where(e => e.Kind != "owin").ToList();
                var response = await client.PostAsync(root + "interop.axd?op=consume",
                    new StringContent(JsonSerializer.Serialize(handled), Encoding.UTF8, "application/json"));
                string body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {body}");
                var verdicts = JsonSerializer.Deserialize<List<InteropResult>>(body);
                foreach (InteropEntry entry in handled)
                {
                    var verdict = verdicts.SingleOrDefault(r => r.Kind == entry.Kind && r.Case == entry.Case);
                    results.Add((entry, verdict == null ? "no result" : verdict.Problem));
                }
                foreach (InteropEntry entry in entries.Where(e => e.Kind == "owin"))
                {
                    if (entry.Value.StartsWith("error: ", StringComparison.Ordinal))
                    {
                        // The other runtime failed: this one must fail alike.
                        string mine = await SignIn(entry.Case);
                        results.Add((entry, mine == entry.Value ? null : $"the other runtime gave {entry.Value}, this one {(mine.StartsWith("error: ", StringComparison.Ordinal) ? mine : "a cookie")}"));
                        continue;
                    }
                    using var request = new HttpRequestMessage(HttpMethod.Get, root + "owin/check?type=" + entry.Case);
                    request.Headers.Add("Cookie", ".AspNet." + entry.Case + "=" + entry.Value);
                    var check = await client.SendAsync(request);
                    string answer = await check.Content.ReadAsStringAsync();
                    results.Add((entry, check.StatusCode == HttpStatusCode.OK && answer == "ok" ? null : $"{(int)check.StatusCode} {answer}"));
                }
                return results;
            }

            public void Dispose()
            {
                client.Dispose();
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(10000);
                }
                process.Dispose();
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
