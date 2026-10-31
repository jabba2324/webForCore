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

        // Web Forms data binding over the application's types: Eval, an auto-generated GridView from an ObjectDataSource
        // and from a DataTable with columns of application types, a DetailsView, a TypeConverter and [MetadataType].
        [Fact]
        public async Task DataBindingOverApplicationTypes()
        {
            string body = await _server.Client.GetStringAsync("/data/Binding.aspx");

            Assert.Contains("<span class=\"book\">Dune|size:L</span><span class=\"book\">Emma|size:M</span>", body);
            Assert.Contains("<th scope=\"col\">Title</th>", body); // ObjectDataSource's columns
            Assert.Contains("<td>Title</td><td>Dune</td>", body); // DetailsView
            Assert.Contains(">String/Book title<", body); // TypeDescriptor and the buddy class's [Display]
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
