using Xunit;

namespace IISGate
{
    // ADO.NET provider registration from configuration: <system.data><DbProviderFactories> in the sample's Web.config,
    // which .NET Framework's DbProviderFactories read and .NET's ignores (webForCore registers them at start).
    [Collection(IISSampleCollection.Name)]
    public class DataTests
    {
        private readonly SampleServer _server;

        public DataTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task ConfiguredProviderFactoriesAreRegistered()
        {
            string body = await _server.Client.GetStringAsync("/data/Providers.aspx");

            Assert.Contains("gate=IISSample.GateDbProviderFactory;", body); // <add>
            Assert.Contains("oledb=False;", body); // <remove> of a provider registered by default
            Assert.Contains("sqlclient=System.Data.SqlClient.SqlClientFactory;", body);
        }
    }
}
