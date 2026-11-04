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
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
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

        // #35: types a shared-framework facade forwards to an assembly the application deploys must be the application's.
        // Subtext's Microsoft.ApplicationBlocks.Data (a .NET Framework 1.1 library) names SqlDataReader in System.Data, and
        // every request failed with "Method not found: 'System.Data.SqlClient.SqlDataReader ...SqlHelper.ExecuteReader(...)'".
        [Fact]
        public async Task SqlClientTypesThroughTheSystemDataFacadeAreTheApplications()
        {
            var (response, body) = await Get("/regressions/DataFacadeForwards.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("same type: True", body);
        }

        // #50: the AppDomain's "DataDirectory" is the site's App_Data, as HttpRuntime sets it on IIS. Bonobo Git Server's
        // System.Data.SQLite expands |DataDirectory| from it, and created its database in bin_dotnet/ (on Windows, a
        // change in bin that restarted the application on every write).
        [Fact]
        public async Task DataDirectoryIsTheSitesAppData()
        {
            var (response, body) = await Get("/regressions/DataDirectory.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("is App_Data: True", body);
        }

        // #87: a partial trust level is accepted and ignored: the application runs in full trust. The sample's Web.config
        // has <trust level="Medium" originUrl=".*" />, as Umbraco 6's development web.config does; every request of the
        // application was a Configuration Error ("Unable to read the security policy file for trust level 'Medium'").
        [Fact]
        public async Task PartialTrustLevelRunsInFullTrust()
        {
            var (response, body) = await Get("/regressions/TrustLevel.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("configured: Medium\n", body);
            Assert.Contains("runtime: Full\n", body);
            Assert.Contains("permission set: (none: full trust)\n", body);
            Assert.Contains("demand: Unrestricted\n", body);
        }

        // #33: Assembly.Load("App_GlobalResources"), as Visual Studio's designer classes for global resources do, resolves
        // to the compiled App_GlobalResources.xxxxxxxx assembly, as on IIS (and "App_Code" and "__code" to App_Code).
        // mojoPortal's Application_Start and every page failed with "Could not load file or assembly 'App_GlobalResources,
        // Culture=neutral, PublicKeyToken=null'".
        [Fact]
        public async Task FixedNamesOfTopLevelAssembliesLoad()
        {
            var (response, body) = await Get("/regressions/FixedAssemblyNames.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("App_Code: App_Code.", body);
            Assert.Contains("App_GlobalResources: App_GlobalResources.", body);
            Assert.Contains("__code: App_Code.", body);
            Assert.Contains("Application_Start: Hello from App_GlobalResources\n", body);
            Assert.Contains("fr: Bonjour depuis App_GlobalResources\n", body);
            Assert.Contains("GetGlobalResourceObject: Hello from App_GlobalResources\n", body);
        }

        // #41: Assembly.GetTypes() on Microsoft's System.Data.SqlClient throws on .NET, Linux and Windows, because of
        // SqlGuidCaster, so Opserver's scan of every loaded assembly for its tab controllers failed on every page.
        // webForCore builds System.Data.SqlClient from Microsoft's source with the type fixed: GetTypes() works on it, and
        // a scan of every assembly loaded in the application, with no workaround, has no failures.
        [Fact]
        public async Task ScanningEveryAssemblyFindsEveryType()
        {
            var (response, body) = await Get("/regressions/AssemblyScan.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("GetTypes: ok, has SqlConnection: True\n", body);
            Assert.Contains("scan finds SqlConnection: True", body);
            Assert.Contains("scan finds this page: True", body);
            Assert.Contains("scan failures: 0\n", body);
            Assert.DoesNotContain("failed: ", body);
        }

        // #90: .aspx and .ascx files without a BOM are read in the ANSI code page when <globalization fileEncoding> isn't
        // set, as .NET Framework's Encoding.Default was (windows-1252 on a Western Windows, and on Linux for an English,
        // Western European or invariant locale). Umbraco 6's installer (install/steps/theend.ascx, byte 0x92) showed
        // "You�re done...now what?" because .NET's Encoding.Default is UTF-8.
        [Fact]
        public async Task FilesWithoutABomAreReadInTheAnsiCodePage()
        {
            var (response, body) = await Get("/regressions/encoding/Ansi.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("�", body);
            if (!body.Contains("fileEncoding: Windows-1252", StringComparison.OrdinalIgnoreCase))
            {
                // A machine whose locale has another code page (932 for Japanese, ...) reads the file in that one.
                Assert.Matches("fileEncoding: (?!utf-8)", body);
                return;
            }
            Assert.Contains("page: You’re done, café", body);
            Assert.Contains("control: You’re done...now what?", body);
        }

        // A file with a UTF-8 BOM is UTF-8 whatever fileEncoding is, and an application whose files are UTF-8 without a
        // BOM sets <globalization fileEncoding="utf-8" />, as it had to on IIS.
        [Theory]
        [InlineData("/regressions/encoding/Bom.aspx", "bom: ")]
        [InlineData("/regressions/encoding/utf8/Utf8.aspx", "utf8: ")]
        public async Task UnicodeFilesAreReadAsUtf8(string url, string prefix)
        {
            var (response, body) = await Get(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(prefix + "You’re done, café", body);
        }

        // #88: <system.codedom> entries naming .NET Framework's Microsoft.CSharp.CSharpCodeProvider and
        // Microsoft.VisualBasic.VBCodeProvider (System, Version=2.0.0.0), as .NET 3.5/4.0 project templates wrote them,
        // compile pages with System.Web's providers and the entries' options, as on IIS. Umbraco 6.2.6's every page
        // failed with "Unable to cast object of type 'Microsoft.CSharp.CSharpCodeProvider' to type
        // 'WebFormsForCore.CodeDom.Compiler.CodeDomProvider'". The sample's Web.config has the entries, so every page
        // of the gate compiles through them.
        [Fact]
        public async Task FrameworkCodeDomProvidersInConfigCompilePages()
        {
            var (response, body) = await Get("/regressions/FrameworkCodeDomProviders.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("c# provider: WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider\n", body);
            Assert.Contains("c# created: WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider\n", body);
            Assert.Contains("c# warning level: 4\n", body);
            Assert.Contains("vb provider: WebFormsForCore.CodeDom.Compiler.VBCodeProvider\n", body);
            Assert.Contains("vb created: WebFormsForCore.CodeDom.Compiler.VBCodeProvider\n", body);

            // A VB page, with the entry's OptionInfer="true" (Dim without As infers Int32).
            (response, body) = await Get("/regressions/FrameworkVBCodeProvider.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("vb page: Int32 42\n", body);
        }

        // #128: Server.MachineName threw DllNotFoundException off Windows (kernel32's GetComputerName). AspNetDocs' Wingtip
        // Toys: ELMAH's /elmah.axd/rss and /elmah.axd/json failed, and /elmah.axd answered with an empty page. On .NET
        // Framework it is GetComputerName's name, as Environment.MachineName and health monitoring's are there.
        [Fact]
        public async Task ServerMachineNameIsTheMachinesName()
        {
            var (response, body) = await Get("/regressions/MachineName.aspx");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("server: " + Environment.MachineName + "\n", body);
            Assert.Contains("wrapper: " + Environment.MachineName + "\n", body);
            Assert.Contains("environment: " + Environment.MachineName + "\n", body);
            Assert.Contains("health monitoring: " + Environment.MachineName + "\n", body);
        }
    }
}
