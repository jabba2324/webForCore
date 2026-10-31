using System.Net;
using Xunit;

namespace IISGate
{
    // Regression tests for problems found by migrating real applications (each test names the issue and the app that
    // hit it). The pages are in test/webForCore.IIS.Sample/regressions, which has its own web.config.
    [Collection(IISSampleCollection.Name)]
    public class RegressionTests
    {
        private readonly SampleServer _server;

        public RegressionTests(SampleServer server)
        {
            _server = server;
        }

        private async Task<(HttpResponseMessage Response, string Body)> Get(string url)
        {
            var response = await _server.Client.GetAsync(url);
            return (response, await response.Content.ReadAsStringAsync());
        }

        // #1: .NET Framework libraries bind ConfigurationManager through System.Configuration 4.0.0.0 and must get the
        // application's (web.config-aware) one. eShopLegacyMVC's Microsoft.AspNet.SessionState module and Bonobo Git
        // Server's WebActivatorEx failed on every request with "AppSettingsSection does not inherit from
        // IConfigurationSectionHandler".
        [Fact]
        public async Task ConfigurationManagerThroughTheFrameworkFacadeReadsWebConfig()
        {
            var (response, body) = await Get("/regressions/ConfigurationFacade.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("same type: True", body);
            Assert.Contains("appSettings: from regressions/web.config", body);
            Assert.Contains("sessionState: System.Web.Configuration.SessionStateSection", body);
        }

        // #2: two CacheDependency objects on one file under different spellings threw "Item has already been added" in
        // FileChangesMonitor on Linux (eShopLegacyMVC: @Styles.Render when two first requests rendered the bundles), and
        // a change to the file must reach both.
        [Fact]
        public async Task TwoDependenciesOnOneFileBothSeeItChange()
        {
            var (response, body) = await Get("/regressions/FileDependency.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("two dependencies: ok", body);
            Assert.Contains("first changed: True", body);
            Assert.Contains("second changed: True", body);
        }

        // #12: <handlers> entries whose path starts with '/' never matched. MiniBlog maps its XML-RPC endpoint
        // (path="/metaweblog"), feeds ("/feed/*") and comment posting ("/comment.ashx") that way: all were 404.
        [Theory]
        [InlineData("/regressions/handler/rss")]
        [InlineData("/regressions/exact-handler")]
        public async Task HandlerPathWithLeadingSlashMatches(string url)
        {
            var (response, body) = await Get(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("gate handler", body);
        }

        // The pattern is still anchored at the application root.
        [Fact]
        public async Task HandlerPathWithLeadingSlashDoesNotMatchDeeper()
        {
            var (response, body) = await Get("/other/regressions/exact-handler");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("gate handler", body);
        }

        // #10: IIS's "Remove WWW" template (patternSyntax="Wildcard", {CACHE_URL}) made webForCore drop every rule of its
        // level, so MiniBlog's rule answering posts/*.xml with 403 stopped blocking: the post files were served. The
        // template itself must work: CACHE_URL is scheme://host:port/path?query, as on IIS.
        [Fact]
        public async Task WildcardRuleWithCacheUrlRedirects()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/regressions/rewrite/public.txt?q=1");
            request.Headers.Host = "www.regressions.test";
            var response = await _server.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal("http://regressions.test:80/regressions/rewrite/public.txt?q=1", response.Headers.Location?.OriginalString);
        }

        [Theory]
        [InlineData("/regressions/rewrite/private-notes.txt")] // patternSyntax="Wildcard"
        [InlineData("/regressions/rewrite/exact.txt")]         // patternSyntax="ExactMatch"
        public async Task RulesNextToAWildcardRuleStillApply(string url)
        {
            var (response, body) = await Get(url);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("file", body);
        }

        [Fact]
        public async Task FilesTheRulesDontBlockAreServed()
        {
            var (response, body) = await Get("/regressions/rewrite/public.txt");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("public file", body);
        }

        // #10: rules webForCore can't use fail closed (500) instead of being skipped.
        [Theory]
        [InlineData("/regressions/rewrite-unusable/blocked.txt")]
        [InlineData("/regressions/rewrite-unusable/")]
        public async Task UnusableRulesFailClosed(string url)
        {
            var (response, body) = await Get(url);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("should be blocked", body);
        }

        // #11: a rewrite to a query string alone (MiniBlog: url="?slug={R:1}" for ^post/(.*)) is to the level's root with
        // that query, as on IIS; it was a bare 500 (IndexOutOfRangeException in ASP.NET Core's RewriteAction).
        [Fact]
        public async Task RewriteToAQueryStringRunsTheLevelsDefaultDocument()
        {
            var (response, body) = await Get("/regressions/rewrite/post/hello-world");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("rewrite default page, slug=hello-world", body);
        }
    }
}
