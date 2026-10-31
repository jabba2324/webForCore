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
    }
}
