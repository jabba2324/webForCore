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

        // #13: with RouteExistingFiles, a route takes a URL that is also a directory, before IIS's courtesy redirect to
        // the trailing slash. Clean Architecture in ASP.NET MVC 5 (feature folders Products/ and Contact/ named like their
        // controllers) got a 301 to /Products/ for every link.
        [Fact]
        public async Task RouteTakesADirectoryUrlWithRouteExistingFiles()
        {
            var (response, body) = await Get("/regressions/routed");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("routed page for /regressions/routed", body);
        }

        // A directory no route takes still gets the courtesy redirect, also one with its own rewrite rules (where the
        // slash used to be added without a redirect).
        [Theory]
        [InlineData("/regressions/unrouted")]
        [InlineData("/regressions/rewrite")]
        public async Task DirectoryWithoutARouteStillRedirects(string url)
        {
            var (response, _) = await Get(url);
            Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
            Assert.Equal(url + "/", response.Headers.Location?.OriginalString);
        }

        // #15: W3C logs (client addresses, every URL with its query string) were written in the site and served: every
        // app the smoke test ran showed them at /logs/W3SVC1/u_ex<date>.0000.txt. The default is now outside the site
        // (tools/ci/package-smoke checks that); a folder an application chooses inside the site, as this sample does
        // (w3c-logs/W3SVC1, a plain folder), is never served.
        [Fact]
        public async Task W3CLogsInTheSiteAreNotServed()
        {
            await Get("/Default.aspx?w3c-regression=1");
            var folder = Path.Combine(_server.SampleDirectory, "w3c-logs", "W3SVC1");
            string file = null;
            for (int i = 0; i < 50 && file == null; i++)
            {
                await Task.Delay(100);
                file = Directory.Exists(folder) ? Directory.GetFiles(folder, "u_ex*").FirstOrDefault() : null;
            }
            Assert.NotNull(file);
            foreach (var url in new[] { "/w3c-logs/W3SVC1/" + Path.GetFileName(file), "/w3c-logs/W3SVC1/", "/w3c-logs/" })
            {
                var (response, body) = await Get(url);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.DoesNotContain("#Fields:", body);
            }
        }

        // #8: Repeater, ListView and GridView with ItemType load System.Web.DynamicData (EnableDynamicData) in OnInit;
        // it wasn't built, so eShopLegacyWebForms' catalog (a ListView with ItemType) failed with FileNotFoundException.
        [Fact]
        public async Task DataControlsWithItemTypeRender()
        {
            var (response, body) = await Get("/regressions/TypedDataControls.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("[repeater alpha]", body);
            Assert.Contains("[listview Second book]", body);
            Assert.Contains("[gridview 1 First book]", body);
        }

        // #3, documented: AppDomain.CurrentDomain.BaseDirectory is the build output (there is one AppDomain per process on
        // .NET), and the APIs docs/MIGRATING.md points to are the site root. eShopLegacyMVC's seed read its SQL scripts
        // through BaseDirectory. If this changes, update docs/NAMESPACES.md and MIGRATING.md.
        [Fact]
        public async Task BaseDirectoryIsTheBuildOutputAndTheSiteApisAreTheSite()
        {
            var (response, body) = await Get("/regressions/SiteFolder.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("AppDomain.CurrentDomain.BaseDirectory: bin_dotnet", body);
            Assert.Contains("HttpRuntime.AppDomainAppPath: webForCore.IIS.Sample", body);
            Assert.Contains("HostingEnvironment.ApplicationPhysicalPath: webForCore.IIS.Sample", body);
            Assert.Contains("Server.MapPath: webForCore.IIS.Sample", body);
        }

        // #19: configuration types reached through System.dll (a facade on .NET) must be the application's. SocialGoal's
        // ELMAH 1.2 section handlers derive from SingleTagSectionHandler in System, Version=2.0.0.0, and every request
        // failed with "Type 'Elmah.ErrorMailSectionHandler' does not inherit from IConfigurationSectionHandler".
        [Fact]
        public async Task SectionHandlerThroughTheSystemFacadeIsTheApplications()
        {
            var (response, body) = await Get("/regressions/SystemFacadeSection.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("same type: True", body);
            Assert.Contains("section: from SingleTagSectionHandler", body);
        }

        // #20: System.Web.Abstractions and System.Web.Routing, the .NET Framework 4 facades, in a web.config's
        // <compilation><assemblies> and bound by .NET 3.5 libraries. Roadkill's web.config lists both, and every request
        // was a Configuration Error ("Could not load file or assembly 'System.Web.Abstractions, Version=4.0.0.0'").
        [Fact]
        public async Task SystemWebAbstractionsAndRoutingFacadesLoad()
        {
            var (response, body) = await Get("/regressions/facades/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("HttpContextBase: True", body);
            Assert.Contains("RouteTable: True", body);
            Assert.Contains("path: ~/regressions/facades/Default.aspx", body);
        }

        // #26: a configSource file in a subfolder, written with '\' as web.configs have it. MVCForum's ImageProcessor
        // sections (configSource="Config\imageprocessor\processing.config") were a Configuration Error on every request:
        // on Linux '\' isn't a separator, and '/' is rejected, as on .NET Framework.
        [Fact]
        public async Task ConfigSourceInASubfolderIsRead()
        {
            var (response, body) = await Get("/regressions/configsource/");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(@"setting: from Config\settings.config", body);
        }
    }
}
