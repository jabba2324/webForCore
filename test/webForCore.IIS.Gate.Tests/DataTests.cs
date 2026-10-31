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

        // A Visual Studio DataSet in App_Code (CatalogData.xsd), compiled with the site: the typed DataSet and its
        // TableAdapters against SQL Server (127.0.0.1,14333: tools/lift/sqlserver.sh locally, a service in CI).
        [Fact]
        public async Task TypedDataSetAndTableAdapters()
        {
            string body = await _server.Client.GetStringAsync("/data/TypedDataSet.aspx?reset=1");

            Assert.Contains("linq=Chai,Chang;", body);              // LINQ over a typed table (TypedTableBase<ProductsRow>)
            Assert.Contains("bycategory=Aniseed Syrup;", body);     // a query with a parameter
            Assert.Contains("count=2;", body);                      // a scalar query
            Assert.Contains("proc=3/4;", body);                     // QueriesTableAdapter: a stored procedure, an output parameter
            Assert.Contains("insert=1;update=1;stale=0;delete=1;", body); // DB-direct methods, optimistic concurrency
            Assert.Contains("ods=1;", body);                        // ObjectDataSource calling Insert by parameter names
            Assert.Contains("updateall=4/False;apple=3/3;children=2;", body); // TableAdapterManager.UpdateAll
            Assert.Contains("<td>Chai</td><td>20.00</td>", body);   // GridView over ObjectDataSource and GetProductsByCategoryID
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
