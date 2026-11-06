using Xunit;

namespace SessionCacheGate
{
    // #210: HttpRuntime.AppDomainAppId is IIS's /LM/W3SVC/<site ID>/ROOT, with the site ID from the host's
    // WebForCore:Host:SiteId (1 by default, IIS's Default Web Site). What is keyed by the application's ID is then shared
    // by the instances of one application that have the same site ID, as the servers of an IIS farm whose site IDs match,
    // and not by applications whose site IDs differ, as two sites on one IIS server: SQL Server session state
    // (ASPStateTempApplications) and <machineKey> keys with IsolateByAppId. The site ID is given on the host's command line
    // here, so the worker has it only from the host.
    public abstract class SiteIdPair : Pair
    {
        public const string Database = "WebForCoreSessionCacheSiteId";

        protected override string ConfigName => "SiteId.config";

        protected abstract int SiteIdOf(string instance);

        protected override IEnumerable<string> HostArguments(string instance) =>
            new[] { "--WebForCore:Host:SiteId=" + SiteIdOf(instance) };

        protected override Task Prepare()
        {
            var (code, output) = Stores.RegSql(Sites, "-S", Stores.SqlServer, "-U", "sa", "-P", Stores.SqlPassword, "-ssadd", "-sstype", "c", "-d", Database);
            Assert.True(code == 0, output);
            return Task.CompletedTask;
        }

        protected override string Replace(string config) => config.Replace("%SQL%", Stores.SqlConnectionString(Database));
    }

    // Two applications on one machine (and one session database) with site IDs of their own.
    public sealed class DifferentSiteIdsPair : SiteIdPair
    {
        public DifferentSiteIdsPair() { }

        protected override int SiteIdOf(string instance) => instance == "A" ? 2 : 3;
    }

    // One application on two servers, with the same site ID (not the default).
    public sealed class SameSiteIdPair : SiteIdPair
    {
        public SameSiteIdPair() { }

        protected override int SiteIdOf(string instance) => 7;
    }

    public abstract class SiteIdTests<TPair> where TPair : SiteIdPair
    {
        protected SiteIdTests(TPair pair)
        {
            Pair = pair;
        }

        protected TPair Pair { get; }

        protected async Task<(string Id, Page Ticket)> SessionAndTicketOnA()
        {
            var page = (await Page.Get(Pair.A, "/Session.aspx?op=set&key=colour&value=blue")).Ok();
            Assert.Equal("True", page["new"]);
            Assert.Equal("SQLServer", page["mode"]);
            var ticket = (await Page.Get(Pair.A, "/Ticket.aspx?op=encrypt&name=ann")).Ok();
            Assert.False(string.IsNullOrEmpty(ticket["ticket"]));
            return (page["id"], ticket);
        }
    }

    [Collection(SessionCacheCollection.Name)]
    public sealed class DifferentSiteIdsTests : SiteIdTests<DifferentSiteIdsPair>, IClassFixture<DifferentSiteIdsPair>
    {
        public DifferentSiteIdsTests(DifferentSiteIdsPair pair) : base(pair) { }

        // As IIS has them for sites 2 and 3: the application's ID, APPL_MD_PATH, INSTANCE_ID and INSTANCE_META_PATH.
        [Fact]
        public async Task TheApplicationsIdHasItsSiteId()
        {
            var a = (await Page.Get(Pair.A, "/Ticket.aspx")).Ok();
            var b = (await Page.Get(Pair.B, "/Ticket.aspx")).Ok();
            Assert.Equal("/LM/W3SVC/2/ROOT", a["appid"]);
            Assert.Equal("/LM/W3SVC/2/ROOT", a["applmdpath"]);
            Assert.Equal("2", a["instanceid"]);
            Assert.Equal("/LM/W3SVC/2", a["instancemetapath"]);
            Assert.Equal("/LM/W3SVC/3/ROOT", b["appid"]);
            Assert.Equal("/LM/W3SVC/3/ROOT", b["applmdpath"]);
            Assert.Equal("3", b["instanceid"]);
            Assert.Equal("/LM/W3SVC/3", b["instancemetapath"]);
        }

        // A's session isn't B's: SQL Server session state keys the session's row by the application's ID.
        [Fact]
        public async Task TheyDontShareSessions()
        {
            var (id, _) = await SessionAndTicketOnA();

            var page = (await Page.Get(Pair.B, "/Session.aspx", id)).Ok();
            Assert.Equal("B", page["instance"]);
            Assert.Equal(id, page["id"]);
            Assert.Equal("True", page["new"]);
            Assert.Null(page["value.colour"]);

            // A still has it.
            page = (await Page.Get(Pair.A, "/Session.aspx", id)).Ok();
            Assert.Equal("False", page["new"]);
            Assert.Equal("blue", page["value.colour"]);
        }

        // <machineKey ... IsolateByAppId>: A's forms ticket doesn't decrypt on B, whose keys are derived from another ID.
        [Fact]
        public async Task TheyDontShareIsolateByAppIdKeys()
        {
            var (_, ticket) = await SessionAndTicketOnA();

            Assert.Equal("ann", (await Page.Get(Pair.A, "/Ticket.aspx?op=decrypt&ticket=" + ticket["ticket"])).Ok()["name"]);
            var b = (await Page.Get(Pair.B, "/Ticket.aspx?op=decrypt&ticket=" + ticket["ticket"])).Ok();
            Assert.NotEqual("ann", b["name"]);
            Assert.True(b["error"] != null || b["name"] == "", b.Body);
        }
    }

    [Collection(SessionCacheCollection.Name)]
    public sealed class SameSiteIdTests : SiteIdTests<SameSiteIdPair>, IClassFixture<SameSiteIdPair>
    {
        public SameSiteIdTests(SameSiteIdPair pair) : base(pair) { }

        [Fact]
        public async Task TheApplicationsIdIsTheSameOnBothInstances()
        {
            var a = (await Page.Get(Pair.A, "/Ticket.aspx")).Ok();
            var b = (await Page.Get(Pair.B, "/Ticket.aspx")).Ok();
            Assert.Equal("/LM/W3SVC/7/ROOT", a["appid"]);
            Assert.Equal("/LM/W3SVC/7/ROOT", a["applmdpath"]);
            Assert.Equal("7", a["instanceid"]);
            Assert.Equal("/LM/W3SVC/7", a["instancemetapath"]);
            Assert.Equal(a["appid"], b["appid"]);
        }

        [Fact]
        public async Task TheyShareSessions()
        {
            var (id, _) = await SessionAndTicketOnA();

            var page = (await Page.Get(Pair.B, "/Session.aspx", id)).Ok();
            Assert.Equal("B", page["instance"]);
            Assert.Equal("False", page["new"]);
            Assert.Equal("blue", page["value.colour"]);
        }

        [Fact]
        public async Task TheyShareIsolateByAppIdKeys()
        {
            var (_, ticket) = await SessionAndTicketOnA();

            var b = (await Page.Get(Pair.B, "/Ticket.aspx?op=decrypt&ticket=" + ticket["ticket"])).Ok();
            Assert.Equal("ann", b["name"]);
        }
    }
}
