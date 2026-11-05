using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using WebForCoreGate;
using Xunit;

namespace SessionCacheGate
{
    // Two copies of test/webForCore.SessionCache.Sample, as two servers would have the site: each in its own folder, so
    // nothing that depends on the folder (the application's ID, its temporary files) is the same on both. Made once for
    // the suite; each configuration writes its Web.config into both and starts a host on each (Pair).
    public sealed class Sites : IDisposable
    {
        public const string Sample = "webForCore.SessionCache.Sample";

        // xUnit 2's class fixtures (the pairs) can't take the collection fixture: they share this one, which the
        // collection fixture (SitesCleanup) deletes when the suite ends.
        private static readonly Lazy<Sites> s_shared = new Lazy<Sites>(() => new Sites());

        public static Sites Shared => s_shared.Value;

        public static void DeleteShared()
        {
            if (s_shared.IsValueCreated) s_shared.Value.Dispose();
        }

        private Sites()
        {
            Repository = WebForCoreHost.Repository(Path.Combine("test", Sample));
            SampleDirectory = Path.Combine(Repository, "test", Sample);
            Root = Directory.CreateTempSubdirectory("webforcore-sessioncache-").FullName;
            A = Path.Combine(Root, "a");
            B = Path.Combine(Root, "b");
            Copy(SampleDirectory, A);
            Copy(SampleDirectory, B);
        }

        public string Repository { get; }

        public string SampleDirectory { get; }

        public string Root { get; }

        public string A { get; }

        public string B { get; }

        public string Config(string name) => File.ReadAllText(Path.Combine(SampleDirectory, "configs", name));

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }
            foreach (var directory in Directory.EnumerateDirectories(from))
            {
                string name = Path.GetFileName(directory);
                if (name == "obj" || name == "configs") continue;
                Copy(directory, Path.Combine(to, name));
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }
    }

    [CollectionDefinition(Name, DisableParallelization = true)]
    public class SessionCacheCollection : ICollectionFixture<SitesCleanup>
    {
        public const string Name = "Session state and output cache";
    }

    public sealed class SitesCleanup : IDisposable
    {
        public void Dispose() => Sites.DeleteShared();
    }

    // One instance: webforcore-host serving one copy of the site.
    public sealed class Instance : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new StringBuilder();
        private readonly string _tempDirectory;

        public Instance(Sites sites, string directory, string name)
        {
            Name = name;
            BaseAddress = new Uri($"http://127.0.0.1:{FreePort()}");
            var start = WebForCoreHost.StartInfo(sites.Repository, directory, Sites.Sample, new Dictionary<string, string>
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.ToString(),
                ["SESSIONCACHE_INSTANCE"] = name,
            }, out _tempDirectory);
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(150),
            };
        }

        public string Name { get; }

        public Uri BaseAddress { get; }

        public HttpClient Client { get; }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        // The first page compiled and served (503 while the worker starts).
        public async Task WaitUntilReady()
        {
            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (true)
            {
                try
                {
                    var response = await Client.GetAsync("/Types.aspx?type=System.Object");
                    if (response.StatusCode == HttpStatusCode.OK) return;
                    if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                    {
                        throw new InvalidOperationException($"{Name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}\n{Output}");
                    }
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline && !_process.HasExited)
                {
                }
                if (DateTime.UtcNow >= deadline || _process.HasExited)
                {
                    throw new InvalidOperationException($"{Name} didn't start:\n{Output}");
                }
                await Task.Delay(250);
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            Client.Dispose();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10000);
            }
            _process.Dispose();
            WebForCoreHost.DeleteTempDirectory(_tempDirectory);
        }
    }

    // Two instances on one store: configs/<name> as both copies' Web.config, with its placeholders replaced.
    public abstract class Pair : IAsyncLifetime
    {
        private readonly Sites _sites;

        protected Pair()
        {
            _sites = Sites.Shared;
        }

        protected Sites Sites => _sites;

        public Instance A { get; private set; }

        public Instance B { get; private set; }

        protected abstract string ConfigName { get; }


        protected virtual Task Prepare() => Task.CompletedTask;

        protected abstract string Replace(string config);

        public async Task InitializeAsync()
        {
            await Prepare();
            string config = Replace(_sites.Config(ConfigName));
            File.WriteAllText(Path.Combine(_sites.A, "Web.config"), config);
            File.WriteAllText(Path.Combine(_sites.B, "Web.config"), config);
            A = new Instance(_sites, _sites.A, "A");
            B = new Instance(_sites, _sites.B, "B");
            await Task.WhenAll(A.WaitUntilReady(), B.WaitUntilReady());
        }

        public Task DisposeAsync()
        {
            A?.Dispose();
            B?.Dispose();
            return Task.CompletedTask;
        }
    }

    // A response of the sample's pages: its status, body, and the "name=value" lines of a text/plain body.
    public sealed class Page
    {
        public HttpStatusCode Status { get; init; }

        public string Body { get; init; }

        public string SetCookie { get; init; }

        public IReadOnlyDictionary<string, string> Values { get; init; }

        public string this[string name] => Values.TryGetValue(name, out var value) ? value : null;

        public long Entered => long.Parse(this["entered"]);

        public long Left => long.Parse(this["left"]);

        public static async Task<Page> Get(Instance instance, string url, string sessionId = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (sessionId != null) request.Headers.Add("Cookie", "ASP.NET_SessionId=" + sessionId);
            using var response = await instance.Client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            var values = new Dictionary<string, string>();
            if (response.Content.Headers.ContentType?.MediaType == "text/plain")
            {
                foreach (var line in body.Split('\n'))
                {
                    int equals = line.IndexOf('=');
                    if (equals > 0) values[line.Substring(0, equals)] = line.Substring(equals + 1);
                }
            }
            return new Page
            {
                Status = response.StatusCode,
                Body = body,
                SetCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies) ? string.Join("\n", cookies) : null,
                Values = values,
            };
        }

        public Page Ok()
        {
            Assert.True(Status == HttpStatusCode.OK, $"{(int)Status}: {Body}");
            return this;
        }
    }

    public static class Stores
    {
        // Redis: WEBFORCORE_REDIS, or 127.0.0.1:16379 (tools/ci/redis.sh locally; in CI a service container on Linux and
        // tools/ci/redis-windows.ps1 on Windows).
        public static string Redis => Environment.GetEnvironmentVariable("WEBFORCORE_REDIS") is { Length: > 0 } redis ? redis : "127.0.0.1:16379";

        // SQL Server as the other suites have it (tools/ci/sqlserver.sh, a service container in CI, SQL Server Express on Windows).
        public const string SqlServer = "127.0.0.1,14333";

        public const string SqlPassword = "webForCore-Test1!";

        public static string SqlConnectionString(string database) =>
            $"Data Source={SqlServer};{(database == null ? "" : "Initial Catalog=" + database + ";")}User ID=sa;Password={SqlPassword};TrustServerCertificate=True";

        public static (int Code, string Output) RegSql(Sites sites, params string[] args)
        {
            var output = new DirectoryInfo(AppContext.BaseDirectory);
            var tool = Path.Combine(sites.Repository, "src", "WebFormsForCore.RegSql", "bin", output.Parent!.Name, output.Name, "webforcore-regsql.dll");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            start.ArgumentList.Add(tool);
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start);
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEndAsync();
            var text = process.StandardOutput.ReadToEnd() + error.Result;
            process.WaitForExit();
            return (process.ExitCode, text);
        }

        public static void Sql(string database, string command)
        {
            using var connection = new System.Data.SqlClient.SqlConnection(SqlConnectionString(database));
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = command;
            cmd.ExecuteNonQuery();
        }
    }
}
