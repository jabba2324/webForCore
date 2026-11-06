using Xunit;

namespace SessionCacheGate
{
    // One instance of the sample with UseAspNetCoreSessionProvider() (SESSIONCACHE_ASPNETCORE_SESSION=1) and configs/<name>
    // as its Web.config.
    public abstract class AspNetCoreSessionSite : IAsyncLifetime
    {
        protected abstract string ConfigName { get; }

        public Instance A { get; private set; }

        public async Task InitializeAsync()
        {
            var sites = Sites.Shared;
            File.WriteAllText(Path.Combine(sites.A, "Web.config"), sites.Config(ConfigName));
            A = new Instance(sites, sites.A, "A", environment: new Dictionary<string, string> { ["SESSIONCACHE_ASPNETCORE_SESSION"] = "1" });
            await A.WaitUntilReady();
        }

        public Task DisposeAsync()
        {
            A?.Dispose();
            return Task.CompletedTask;
        }
    }

    public sealed class AspNetCoreSessionOffSite : AspNetCoreSessionSite
    {
        public AspNetCoreSessionOffSite() { }

        protected override string ConfigName => "AspNetCoreSessionOff.config";
    }

    public sealed class AspNetCoreSessionOnSite : AspNetCoreSessionSite
    {
        public AspNetCoreSessionOnSite() { }

        protected override string ConfigName => "AspNetCoreSession.config";
    }

    static class SessionProbes
    {
        public static async Task<Page> Probe(Instance instance, string url)
        {
            var page = (await Page.Get(instance, url)).Ok();
            Assert.NotNull(page["session"]);
            return page;
        }

        public static void HasNoSession(Page page)
        {
            Assert.Equal("null", page["session"]);
            Assert.True(page.SetCookie == null || !page.SetCookie.Contains("ASP.NET_SessionId"), page.SetCookie);
        }
    }

    // #185: UseAspNetCoreSessionProvider() replaces the store Web.config names, and nothing else. On IIS
    // <sessionState mode="Off"> turns session state off: HttpContext.Session is null and no session cookie is sent. The
    // provider turned it on there.
    [Collection(SessionCacheCollection.Name)]
    public sealed class AspNetCoreSessionOffTests : IClassFixture<AspNetCoreSessionOffSite>
    {
        private readonly AspNetCoreSessionOffSite _site;

        public AspNetCoreSessionOffTests(AspNetCoreSessionOffSite site)
        {
            _site = site;
        }

        [Theory]
        [InlineData("/HasSession.aspx")]
        [InlineData("/mvc/SessionProbe")]
        public async Task ModeOffWinsOverTheProvider(string url)
        {
            SessionProbes.HasNoSession(await SessionProbes.Probe(_site.A, url));
        }
    }

    // With the provider and session state on, a page with EnableSessionState="false", the pages of a folder with
    // <pages enableSessionState="false">, and an MVC controller with [SessionState(SessionStateBehavior.Disabled)] have
    // none, as on IIS; the others have the provider's.
    [Collection(SessionCacheCollection.Name)]
    public sealed class AspNetCoreSessionOnTests : IClassFixture<AspNetCoreSessionOnSite>
    {
        private readonly AspNetCoreSessionOnSite _site;

        public AspNetCoreSessionOnTests(AspNetCoreSessionOnSite site)
        {
            _site = site;
        }

        [Theory]
        [InlineData("/HasSession.aspx")]
        [InlineData("/mvc/SessionProbe")]
        public async Task TheProviderGivesSessionState(string url)
        {
            var page = await SessionProbes.Probe(_site.A, url);
            Assert.Equal("present", page["session"]);
            Assert.Contains("ASP.NET_SessionId=" + page["id"], page.SetCookie);
        }

        [Theory]
        [InlineData("/NoSession.aspx")]          // <%@ Page EnableSessionState="False" %>
        [InlineData("/PagesOff/HasSession.aspx")] // <location path="PagesOff"><pages enableSessionState="false">
        [InlineData("/mvc/Sessionless")]          // [SessionState(SessionStateBehavior.Disabled)]
        public async Task TurnedOffWhereThePageOrControllerSays(string url)
        {
            SessionProbes.HasNoSession(await SessionProbes.Probe(_site.A, url));
        }
    }
}
