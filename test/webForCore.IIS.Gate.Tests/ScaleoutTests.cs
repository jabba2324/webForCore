using System.Data.SqlClient;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNet.SignalR.Client;
using Microsoft.AspNet.SignalR.Client.Http;
using Microsoft.AspNet.SignalR.Client.Transports;
using WebForCoreGate;
using Xunit;

namespace IISGate
{
    // SignalR 2 scale-out: two instances of one application (test/webForCore.Scaleout.Sample), each under its own
    // webforcore-host, as two servers of a web farm, on one backplane: StackExchange.Redis 2
    // (Microsoft.AspNet.SignalR.StackExchangeRedis), StackExchange.Redis.StrongName 1.x (Microsoft.AspNet.SignalR.Redis)
    // and SQL Server (Microsoft.AspNet.SignalR.SqlServer). A message a hub sends on one instance reaches the clients of
    // the other, and so do groups; a client of an instance that restarts reconnects to it and is still in its groups;
    // and a long-polling client whose requests alternate between the instances, as a load balancer without sticky
    // sessions sends them, works. Redis at 127.0.0.1:16379 (tools/ci/redis.sh locally, a service in CI), SQL Server at
    // 127.0.0.1,14333 (tools/ci/sqlserver.sh), in a database of the test's own, dropped at the end.
    public abstract class ScaleoutTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private readonly ScaleoutFarm _farm;

        protected ScaleoutTests(ScaleoutFarm farm)
        {
            _farm = farm;
        }

        // A client of the hub; its proxy is made before the connection starts, as the client requires.
        private sealed class Client : IDisposable
        {
            public Client(Uri url, Action<IHubProxy> setup)
            {
                Connection = new HubConnection(url.ToString());
                Hub = Connection.CreateHubProxy("ScaleoutHub");
                setup(Hub);
            }

            public HubConnection Connection { get; }

            public IHubProxy Hub { get; }

            public void Dispose()
            {
                Connection.Stop();
                Connection.Dispose();
            }
        }

        private static async Task<Client> Connect(Uri url, IClientTransport transport, Action<IHubProxy> setup)
        {
            var client = new Client(url, setup);
            await client.Connection.Start(transport).WaitAsync(Timeout);
            return client;
        }

        private static async Task<T> Received<T>(TaskCompletionSource<T> received, string what)
        {
            var done = await Task.WhenAny(received.Task, Task.Delay(Timeout));
            Assert.True(done == received.Task, "not received in time: " + what);
            return await received.Task;
        }

        [Fact]
        public async Task BroadcastFromOneInstanceReachesClientsOfBoth()
        {
            var onA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var onB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            string message = "hello " + Guid.NewGuid().ToString("N");
            using var a = await Connect(_farm.A.BaseAddress, new WebSocketTransport(), hub =>
                hub.On<string, string>("broadcast", (m, from) => { if (m == message) onA.TrySetResult(from); }));
            using var b = await Connect(_farm.B.BaseAddress, new WebSocketTransport(), hub =>
                hub.On<string, string>("broadcast", (m, from) => { if (m == message) onB.TrySetResult(from); }));
            var hubA = a.Hub;
            var hubB = b.Hub;
            Assert.Equal("A", await hubA.Invoke<string>("Whoami").WaitAsync(Timeout));
            Assert.Equal("B", await hubB.Invoke<string>("Whoami").WaitAsync(Timeout));

            await hubB.Invoke("Broadcast", message).WaitAsync(Timeout);

            // Sent by the hub on B, to A's client through the backplane, and to B's own.
            Assert.Equal("B", await Received(onA, "the broadcast on A"));
            Assert.Equal("B", await Received(onB, "the broadcast on B"));
        }

        // A group has members on both instances: a message to it from either reaches them all, and not a client outside it.
        [Fact]
        public async Task GroupsSpanInstances()
        {
            string group = "g-" + Guid.NewGuid().ToString("N");
            var memberOnA = new List<string>();
            var memberOnB = new List<string>();
            var outsider = new List<string>();
            var aGotTwo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var bGotTwo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<IHubProxy> record(List<string> list, TaskCompletionSource<bool> two) => hub =>
                hub.On<string, string, string>("groupMessage", (g, m, from) =>
                {
                    lock (list)
                    {
                        list.Add(g + ":" + m + ":" + from);
                        if (list.Count == 2) two?.TrySetResult(true);
                    }
                });

            using var a = await Connect(_farm.A.BaseAddress, new ServerSentEventsTransport(), record(memberOnA, aGotTwo));
            using var b = await Connect(_farm.B.BaseAddress, new LongPollingTransport(), record(memberOnB, bGotTwo));
            using var other = await Connect(_farm.B.BaseAddress, new WebSocketTransport(), record(outsider, null));
            await a.Hub.Invoke("Join", group).WaitAsync(Timeout);
            await b.Hub.Invoke("Join", group).WaitAsync(Timeout);

            // From the outsider's instance (B), then from A.
            await other.Hub.Invoke("SendToGroup", group, "one").WaitAsync(Timeout);
            await a.Hub.Invoke("SendToGroup", group, "two").WaitAsync(Timeout);

            await Received(aGotTwo, "both group messages on A");
            await Received(bGotTwo, "both group messages on B");
            // Each message once. A backplane keeps the order of the messages of one stream, not between the instances'
            // (SQL Server's are in two tables here): "two" may come first.
            lock (memberOnA) Assert.Equal(new[] { group + ":one:B", group + ":two:A" }, memberOnA.OrderBy(m => m, StringComparer.Ordinal));
            lock (memberOnB) Assert.Equal(new[] { group + ":one:B", group + ":two:A" }, memberOnB.OrderBy(m => m, StringComparer.Ordinal));

            // By the time both members have both messages, the outsider would have had them too.
            await Task.Delay(1000);
            lock (outsider) Assert.Empty(outsider);
        }

        // Instance A's process ends and a new one starts in its place, on the same address (the server restarts). Its
        // client reconnects, with its connection token and groups (read with the farm's machine keys), and gets messages
        // sent from B again, to everyone and to its group.
        [Fact]
        public async Task ClientReconnectsWhenItsInstanceRestarts()
        {
            string group = "r-" + Guid.NewGuid().ToString("N");
            var reconnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reconnecting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var broadcast = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var toGroup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            string after = "after restart " + Guid.NewGuid().ToString("N");
            using var a = await Connect(_farm.A.BaseAddress, new WebSocketTransport(), hub =>
            {
                hub.On<string, string>("broadcast", (m, from) => { if (m == after) broadcast.TrySetResult(from); });
                hub.On<string, string, string>("groupMessage", (g, m, from) => { if (g == group && m == after) toGroup.TrySetResult(from); });
            });
            a.Connection.Reconnecting += () => reconnecting.TrySetResult(true);
            a.Connection.Reconnected += () => reconnected.TrySetResult(true);
            var hubA = a.Hub;
            await hubA.Invoke("Join", group).WaitAsync(Timeout);
            string connectionId = a.Connection.ConnectionId;

            _farm.A.Restart();
            await Received(reconnecting, "Reconnecting");
            await Received(reconnected, "Reconnected (state " + a.Connection.State + ")\n" + _farm.A.Output);
            Assert.Equal(ConnectionState.Connected, a.Connection.State);
            Assert.Equal(connectionId, a.Connection.ConnectionId);
            Assert.Equal("A", await hubA.Invoke<string>("Whoami").WaitAsync(Timeout));

            using var b = await Connect(_farm.B.BaseAddress, new WebSocketTransport(), hub => { });
            var hubB = b.Hub;
            await hubB.Invoke("Broadcast", after).WaitAsync(Timeout);
            await hubB.Invoke("SendToGroup", group, after).WaitAsync(Timeout);
            Assert.Equal("B", await Received(broadcast, "the broadcast after the restart"));
            Assert.Equal("B", await Received(toGroup, "the group message after the restart"));
        }

        // No sticky sessions: a long-polling client's requests alternate between the instances, negotiate and connect on
        // one, sends and polls on either. It gets its messages and its hub calls are answered, by both instances.
        [Fact]
        public async Task NoStickySessions()
        {
            var http = new RoundRobinHttpClient(_farm.A.BaseAddress, _farm.B.BaseAddress);
            var answeredBy = new HashSet<string>();
            var received = new List<string>();
            var gotAll = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            const int count = 6;
            string prefix = Guid.NewGuid().ToString("N") + " ";
            using var connection = await Connect(_farm.A.BaseAddress, new LongPollingTransport(http), hub =>
                hub.On<string, string>("broadcast", (m, from) =>
                {
                    if (!m.StartsWith(prefix)) return;
                    lock (received)
                    {
                        received.Add(m.Substring(prefix.Length));
                        if (received.Count == count) gotAll.TrySetResult(true);
                    }
                }));
            var hub = connection.Hub;
            for (int i = 0; i < count; i++)
            {
                // Two calls, then a broadcast: sends alternate between the instances, so the calls go to both.
                answeredBy.Add(await hub.Invoke<string>("Whoami").WaitAsync(Timeout));
                answeredBy.Add(await hub.Invoke<string>("Whoami").WaitAsync(Timeout));
                await hub.Invoke("Broadcast", prefix + i).WaitAsync(Timeout);
            }
            await Received(gotAll, "every broadcast");
            lock (received) Assert.Equal(Enumerable.Range(0, count).Select(i => i.ToString()), received.OrderBy(int.Parse));
            Assert.Equal(new[] { "A", "B" }, answeredBy.OrderBy(x => x));
            Assert.True(http.Hosts.Count == 2, string.Join(", ", http.Hosts));
        }

        // SignalR's HTTP client with each request sent to the next of the instances, as a round-robin load balancer would;
        // each kind of request (negotiate, connect, poll, send, ...) takes its turns of its own, so that sends alternate
        // although each one comes between two polls.
        private sealed class RoundRobinHttpClient : DefaultHttpClient
        {
            private readonly Uri[] _targets;

            public RoundRobinHttpClient(params Uri[] targets)
            {
                _targets = targets;
            }

            public HashSet<string> Hosts { get; } = new HashSet<string>();

            protected override HttpMessageHandler CreateHandler() => new Rewriter(this, base.CreateHandler());

            private sealed class Rewriter : DelegatingHandler
            {
                private readonly RoundRobinHttpClient _owner;
                private readonly Dictionary<string, int> _next = new Dictionary<string, int>();

                public Rewriter(RoundRobinHttpClient owner, HttpMessageHandler inner) : base(inner)
                {
                    _owner = owner;
                }

                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    string kind = request.RequestUri.AbsolutePath.TrimEnd('/');
                    kind = kind.Substring(kind.LastIndexOf('/') + 1);
                    int turn;
                    lock (_next)
                    {
                        _next.TryGetValue(kind, out turn);
                        _next[kind] = turn + 1;
                    }
                    var target = _owner._targets[turn % _owner._targets.Length];
                    request.RequestUri = new UriBuilder(request.RequestUri) { Host = target.Host, Port = target.Port }.Uri;
                    lock (_owner.Hosts) _owner.Hosts.Add(target.Authority);
                    return base.SendAsync(request, cancellationToken);
                }
            }
        }
    }

    [Collection(ScaleoutCollection.Name)]
    public class StackExchangeRedisScaleoutTests : ScaleoutTests, IClassFixture<StackExchangeRedisScaleoutTests.Farm>
    {
        public StackExchangeRedisScaleoutTests(Farm farm) : base(farm)
        {
        }

        public sealed class Farm : ScaleoutFarm
        {
            public Farm() : base("stackexchangeredis", ScaleoutFarm.RedisConnection)
            {
            }
        }
    }

    [Collection(ScaleoutCollection.Name)]
    public class RedisScaleoutTests : ScaleoutTests, IClassFixture<RedisScaleoutTests.Farm>
    {
        public RedisScaleoutTests(Farm farm) : base(farm)
        {
        }

        public sealed class Farm : ScaleoutFarm
        {
            public Farm() : base("redis", ScaleoutFarm.RedisConnection)
            {
            }
        }
    }

    [Collection(ScaleoutCollection.Name)]
    public class SqlServerScaleoutTests : ScaleoutTests, IClassFixture<SqlServerScaleoutTests.Farm>
    {
        public SqlServerScaleoutTests(Farm farm) : base(farm)
        {
        }

        public sealed class Farm : ScaleoutFarm
        {
            public Farm() : base("sqlserver", null)
            {
            }
        }
    }

    // Without a backplane each instance has its own message bus, as on IIS: a broadcast reaches the clients of the
    // instance that sent it only. What the backplanes above fix.
    [Collection(ScaleoutCollection.Name)]
    public class NoBackplaneScaleoutTests : IClassFixture<NoBackplaneScaleoutTests.Farm>
    {
        private readonly Farm _farm;

        public NoBackplaneScaleoutTests(Farm farm)
        {
            _farm = farm;
        }

        [Fact]
        public async Task BroadcastStaysOnItsInstance()
        {
            string message = "alone " + Guid.NewGuid().ToString("N");
            var onA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var onB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var a = new HubConnection(_farm.A.BaseAddress.ToString());
            a.CreateHubProxy("ScaleoutHub").On<string, string>("broadcast", (m, from) => { if (m == message) onA.TrySetResult(from); });
            using var b = new HubConnection(_farm.B.BaseAddress.ToString());
            var hubB = b.CreateHubProxy("ScaleoutHub");
            hubB.On<string, string>("broadcast", (m, from) => { if (m == message) onB.TrySetResult(from); });
            await a.Start(new WebSocketTransport()).WaitAsync(TimeSpan.FromSeconds(30));
            await b.Start(new WebSocketTransport()).WaitAsync(TimeSpan.FromSeconds(30));

            await hubB.Invoke("Broadcast", message).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal("B", await onB.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            await Task.Delay(2000);
            Assert.False(onA.Task.IsCompleted);
            a.Stop();
            b.Stop();
        }

        public sealed class Farm : ScaleoutFarm
        {
            public Farm() : base("", null)
            {
            }
        }
    }

    // The scale-out classes run one after another, each with its two instances, so that one farm runs at a time, and
    // after the other collections, not beside them: four more processes (two hosts, two workers) next to the samples'
    // would make the suite's load, and its use of inotify instances on Linux, higher than it needs to be.
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ScaleoutCollection
    {
        public const string Name = "Scale-out";
    }

    // Two instances, A and B, on one backplane.
    public abstract class ScaleoutFarm : IDisposable
    {
        public const string RedisConnection = "127.0.0.1:16379";
        private const string SqlServer = "Data Source=127.0.0.1,14333;User ID=sa;Password=webForCore-Test1!;TrustServerCertificate=True";

        private readonly string _database;

        protected ScaleoutFarm(string backplane, string connection)
        {
            string key = "webforcore-scaleout-" + Guid.NewGuid().ToString("N");
            if (backplane == "sqlserver")
            {
                // A database of its own, with Service Broker on for SqlDependency (query notifications), as Microsoft's
                // documentation asks of a SignalR backplane's database.
                _database = "WebForCoreScaleout_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                Execute("CREATE DATABASE [" + _database + "]; ALTER DATABASE [" + _database + "] SET ENABLE_BROKER;");
                connection = SqlServer + ";Initial Catalog=" + _database;
            }
            var settings = new Dictionary<string, string>
            {
                ["SCALEOUT_BACKPLANE"] = backplane,
                ["SCALEOUT_CONNECTION"] = connection,
                ["SCALEOUT_KEY"] = key,
            };
            // xunit doesn't dispose a fixture whose constructor throws: what was started is stopped here.
            try
            {
                A = new ScaleoutInstance("A", settings);
                B = new ScaleoutInstance("B", settings);
            }
            catch
            {
                A?.Dispose();
                DropDatabase();
                throw;
            }
        }

        public ScaleoutInstance A { get; }

        public ScaleoutInstance B { get; }

        private static void Execute(string sql)
        {
            using var connection = new SqlConnection(SqlServer);
            connection.Open();
            using var command = new SqlCommand(sql, connection);
            command.ExecuteNonQuery();
        }

        public void Dispose()
        {
            A.Dispose();
            B.Dispose();
            DropDatabase();
        }

        private void DropDatabase()
        {
            if (_database != null)
            {
                SqlConnection.ClearAllPools();
                Execute("ALTER DATABASE [" + _database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + _database + "];");
            }
        }
    }

    // One instance: test/webForCore.Scaleout.Sample under webforcore-host, on an address of its own that a restart keeps.
    public sealed class ScaleoutInstance : IDisposable
    {
        private const string Sample = "webForCore.Scaleout.Sample";
        private readonly string _name;
        private readonly IDictionary<string, string> _settings;
        private readonly StringBuilder _output = new StringBuilder();
        private Process _process;
        private string _tempDirectory;

        public ScaleoutInstance(string name, IDictionary<string, string> settings)
        {
            _name = name;
            _settings = settings;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            listener.Stop();
            Start();
        }

        public Uri BaseAddress { get; }

        public string Output
        {
            get { lock (_output) return _output.ToString(); }
        }

        private void Start()
        {
            string repository = WebForCoreHost.Repository(Path.Combine("test", Sample));
            var settings = new Dictionary<string, string>(_settings)
            {
                ["Kestrel__Endpoints__Http__Url"] = BaseAddress.ToString(),
                ["SCALEOUT_INSTANCE"] = _name,
                // The host's and the workers' configuration reload polls instead of taking inotify instances.
                ["DOTNET_USE_POLLING_FILE_WATCHER"] = "true",
            };
            var start = WebForCoreHost.StartInfo(repository, Path.Combine(repository, "test", Sample), Sample, settings, out _tempDirectory);
            _process = Process.Start(start);
            _process.OutputDataReceived += (s, e) => { lock (_output) _output.AppendLine(_name + ": " + e.Data); };
            _process.ErrorDataReceived += (s, e) => { lock (_output) _output.AppendLine(_name + ": " + e.Data); };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // Up when SignalR answers: the application has started, with its backplane.
            using var client = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
            var deadline = DateTime.UtcNow.AddSeconds(120);
            HttpStatusCode status = 0;
            while (DateTime.UtcNow < deadline && !_process.HasExited)
            {
                try
                {
                    status = client.GetAsync("signalr/hubs").GetAwaiter().GetResult().StatusCode;
                    if (status == HttpStatusCode.OK) return;
                }
                catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
                {
                }
                Thread.Sleep(250);
            }
            string output = Output;
            Stop();
            throw new InvalidOperationException($"instance {_name} didn't start ({status}):\n{output}");
        }

        // The server restarts: its process tree ends, and a new one starts on the same address.
        public void Restart()
        {
            Stop();
            Start();
        }

        private void Stop()
        {
            if (_process == null) return;
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10000);
            }
            _process.Dispose();
            _process = null;
            WebForCoreHost.DeleteTempDirectory(_tempDirectory);
        }

        public void Dispose() => Stop();
    }
}
