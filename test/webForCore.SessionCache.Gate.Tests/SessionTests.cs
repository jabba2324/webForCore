using System.Net;
using Xunit;

namespace SessionCacheGate
{
    // Session state shared by two instances through a store, as two IIS servers share it: what one sets, the other reads;
    // a request holds the session's exclusive lock and a request for the same session on the other instance waits for it
    // (read-only ones too, but not for each other); Session.Abandon on one ends it for both; a session ends Session.Timeout
    // minutes after its last request; a value that isn't serializable fails the request (#180). The same tests for each
    // store (RedisSessionTests, SqlServerSessionTests, SqlAsyncSessionTests).
    public abstract class SessionTests<TPair> where TPair : Pair
    {
        protected SessionTests(TPair pair)
        {
            Pair = pair;
        }

        protected TPair Pair { get; }

        protected Instance A => Pair.A;

        protected Instance B => Pair.B;

        protected abstract string Mode { get; }

        // A new session on A with key=value.
        protected async Task<string> NewSession(string key = "start", string value = "1")
        {
            var page = (await Page.Get(A, $"/Session.aspx?op=set&key={key}&value={value}")).Ok();
            Assert.Equal("True", page["new"]);
            Assert.Equal(Mode, page["mode"]);
            Assert.Contains("ASP.NET_SessionId=" + page["id"], page.SetCookie);
            return page["id"];
        }

        // The application's ID is IIS's metabase path of the site's root (APPL_MD_PATH): the same on every server, wherever
        // the site's folder is. SQL Server session state keys sessions by it (ASPStateTempApplications,
        // SqlSessionStateProviderAsync's suffix), so the servers of a farm share sessions only if it is. It was a hash of
        // the folder's path: two servers never shared a session through SQL Server.
        [Fact]
        public async Task TheApplicationsIdIsTheSameOnBothInstances()
        {
            var a = (await Page.Get(A, "/ReadOnlySession.aspx")).Ok();
            var b = (await Page.Get(B, "/ReadOnlySession.aspx")).Ok();
            Assert.Equal("/LM/W3SVC/1/ROOT", a["appid"]);
            Assert.Equal(a["applmdpath"], a["appid"]);
            Assert.Equal(a["appid"], b["appid"]);
        }

        [Fact]
        public async Task AValueSetOnOneInstanceIsReadOnTheOther()
        {
            string id = await NewSession("colour", "blue");

            var page = (await Page.Get(B, "/Session.aspx", id)).Ok();
            Assert.Equal("B", page["instance"]);
            Assert.Equal(id, page["id"]);
            Assert.Equal("False", page["new"]);
            Assert.Equal("blue", page["value.colour"]);

            // A [Serializable] object, changed on the other instance.
            (await Page.Get(B, "/Session.aspx?op=setbasket&key=basket&owner=ann&items=2", id)).Ok();
            (await Page.Get(A, "/Session.aspx?op=addtobasket&key=basket", id)).Ok();
            page = (await Page.Get(B, "/Session.aspx", id)).Ok();
            Assert.Equal("Basket(ann,3)", page["value.basket"]);
            Assert.Equal("2", page["count"]);
        }

        // A request holds the session's exclusive lock until it ends: one for the same session on the other instance starts
        // only when it has ended, as IIS's session state module and these providers do on .NET Framework.
        [Theory]
        [InlineData("/Session.aspx")]
        [InlineData("/ReadOnlySession.aspx")]
        public async Task ARequestWaitsForTheSessionsLockHeldOnTheOtherInstance(string waiting)
        {
            string id = await NewSession();

            var holding = Page.Get(A, "/Session.aspx?op=hold&ms=3000", id);
            await Task.Delay(1000);
            var waited = await Page.Get(B, waiting, id);
            var held = await holding;

            held.Ok();
            waited.Ok();
            Assert.True(waited.Entered >= held.Left,
                $"{waiting} on B ran from {waited.Entered} while A held the session until {held.Left}");
            Assert.NotNull(waited["value.held"]);
        }

        // Read-only requests (EnableSessionState="ReadOnly") don't take the lock: they run side by side.
        [Fact]
        public async Task ReadOnlyRequestsDontWaitForEachOther()
        {
            string id = await NewSession();

            var holding = Page.Get(A, "/ReadOnlySession.aspx?op=hold&ms=3000", id);
            await Task.Delay(1000);
            var other = (await Page.Get(B, "/ReadOnlySession.aspx", id)).Ok();
            var held = (await holding).Ok();

            Assert.True(other.Left < held.Left, $"B's read-only request ended at {other.Left}, after A's at {held.Left}");
            Assert.Equal("True", other["readonly"]);
            Assert.Equal("1", other["value.start"]);
        }

        [Fact]
        public async Task SessionAbandonOnOneInstanceEndsTheSessionOnBoth()
        {
            string id = await NewSession("name", "ann");

            var page = (await Page.Get(B, "/Session.aspx?op=abandon", id)).Ok();
            Assert.Equal("ann", page["value.name"]);

            page = (await Page.Get(A, "/Session.aspx", id)).Ok();
            // A new session with the same ID, without the old one's values. (Not necessarily empty: the async module stores
            // an uninitialized item for an ID it doesn't find, and the Redis provider marks it with a "SessionStateActions"
            // value, which the first request sees, as on IIS.)
            Assert.Equal("True", page["new"]);
            Assert.Null(page["value.name"]);
        }

        // Session.Timeout = 1: a minute after its last request the session is gone, on both instances.
        [Fact]
        public async Task ASessionEndsItsTimeoutAfterItsLastRequest()
        {
            string id = await NewSession("name", "ann");
            var page = (await Page.Get(B, "/Session.aspx?op=timeout&minutes=1", id)).Ok();
            Assert.Equal("1", page["timeout"]);

            await Task.Delay(TimeSpan.FromSeconds(30));
            page = (await Page.Get(A, "/ReadOnlySession.aspx", id)).Ok();
            Assert.Equal("ann", page["value.name"]);
            Assert.Equal("1", page["timeout"]);

            await Task.Delay(TimeSpan.FromSeconds(65));
            await Expire();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                page = (await Page.Get(DateTime.UtcNow.Second % 2 == 0 ? A : B, "/ReadOnlySession.aspx", id)).Ok();
                if (page["value.name"] == null || DateTime.UtcNow > deadline) break;
                await Task.Delay(1000);
            }
            Assert.Null(page["value.name"]);
            Assert.Equal("True", page["new"]);
        }

        // What removes expired sessions from the store, if it isn't the store itself: SQL Server Agent's job for
        // <sessionState mode="SQLServer"> (SQL Server Express and the Linux container have no agent running).
        protected virtual Task Expire() => Task.CompletedTask;

        // #180: out of process, every session value is serialized at the end of the request, and one that can't be fails it
        // with ASP.NET's error, as on IIS. The session keeps what it had.
        [Fact]
        public async Task AValueThatIsntSerializableFailsTheRequest()
        {
            string id = await NewSession("name", "ann");

            var failed = await Page.Get(B, "/Session.aspx?op=setnotserializable&key=bad", id);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.Status);
            Assert.Contains("Unable to serialize the session state. In 'StateServer' and 'SQLServer' mode, ASP.NET will serialize the session state objects, and as a result non-serializable objects or MarshalByRef objects are not permitted.", failed.Body);
            Assert.Contains("SessionCacheSample.NotSerializableValue", failed.Body);

            var page = (await Page.Get(A, "/Session.aspx", id)).Ok();
            Assert.Equal("ann", page["value.name"]);
            Assert.Null(page["value.bad"]);
        }
    }

    public sealed class RedisPair : Pair
    {
        public RedisPair() { }

        protected override string ConfigName => "Redis.config";

        protected override string Replace(string config) => config.Replace("%REDIS%", Stores.Redis);
    }

    // Microsoft.Web.RedisSessionStateProvider 5.0.4 with the async session state module (SessionStateModuleAsync).
    [Collection(SessionCacheCollection.Name)]
    public sealed class RedisSessionTests : SessionTests<RedisPair>, IClassFixture<RedisPair>
    {
        public RedisSessionTests(RedisPair pair) : base(pair) { }

        protected override string Mode => "Custom";
    }

    // System.Web's <sessionState mode="SQLServer"> (SqlSessionStateStore) in a database webforcore-regsql -ssadd made.
    public sealed class SqlServerPair : Pair
    {
        public const string Database = "WebForCoreSessionCacheSql";

        public SqlServerPair() { }

        protected override string ConfigName => "SqlServer.config";

        protected override Task Prepare()
        {
            var (code, output) = Stores.RegSql(Sites, "-S", Stores.SqlServer, "-U", "sa", "-P", Stores.SqlPassword, "-ssadd", "-sstype", "c", "-d", Database);
            Assert.True(code == 0, output);
            return Task.CompletedTask;
        }

        protected override string Replace(string config) => config.Replace("%SQL%", Stores.SqlConnectionString(Database));
    }

    [Collection(SessionCacheCollection.Name)]
    public sealed class SqlServerSessionTests : SessionTests<SqlServerPair>, IClassFixture<SqlServerPair>
    {
        public SqlServerSessionTests(SqlServerPair pair) : base(pair) { }

        protected override string Mode => "SQLServer";

        // The job aspnet_regsql adds to SQL Server Agent runs DeleteExpiredSessions every minute.
        protected override Task Expire()
        {
            Stores.Sql(SqlServerPair.Database, "EXECUTE dbo.DeleteExpiredSessions");
            return Task.CompletedTask;
        }
    }

    // Microsoft.AspNet.SessionState.SqlSessionStateProviderAsync 2.0.0 with the async module, in a database of its own
    // (the provider creates its table).
    public sealed class SqlAsyncPair : Pair
    {
        public const string Database = "WebForCoreSessionCacheAsync";

        public SqlAsyncPair() { }

        protected override string ConfigName => "SqlAsync.config";

        protected override Task Prepare()
        {
            Stores.Sql(null, $"IF DB_ID('{Database}') IS NULL CREATE DATABASE {Database}");
            return Task.CompletedTask;
        }

        protected override string Replace(string config) => config.Replace("%SQL%", Stores.SqlConnectionString(Database));
    }

    [Collection(SessionCacheCollection.Name)]
    public sealed class SqlAsyncSessionTests : SessionTests<SqlAsyncPair>, IClassFixture<SqlAsyncPair>
    {
        public SqlAsyncSessionTests(SqlAsyncPair pair) : base(pair) { }

        protected override string Mode => "Custom";

        // The provider deletes expired sessions at the end of a request, at most every 30 seconds, and reading a session
        // renews it: other users' requests (here, without a session) remove it, on each instance, before its own user
        // comes back.
        protected override async Task Expire()
        {
            (await Page.Get(A, "/ReadOnlySession.aspx")).Ok();
            (await Page.Get(B, "/ReadOnlySession.aspx")).Ok();
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}
