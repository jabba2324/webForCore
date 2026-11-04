using System.Linq;
using System.Web.Mvc;
using Microsoft.AspNet.OData.Builder;
using Microsoft.OData.Edm;

namespace SmokeMvc.Controllers
{
    // Issue #142: Web API OData's ODataConventionModelBuilder (Microsoft.AspNet.OData 7.5, built for .NET Framework) maps
    // System.Data.Linq.Binary as Edm.Binary, so building any model loads System.Data.Linq. Without LINQ to SQL,
    // GetEdmModel() threw FileNotFoundException for System.Data.Linq, 4.0.0.0, and an OData application failed in
    // Application_Start (the smoke test's odata-samples-compositekey, F68). The webForCore package brings it.
    public class ODataModelController : Controller
    {
        public ContentResult Index()
        {
            var builder = new ODataConventionModelBuilder();
            builder.EntitySet<Document>("Documents");
            IEdmModel model = builder.GetEdmModel();
            var type = model.SchemaElements.OfType<IEdmEntityType>().Single();
            return Content(string.Join(", ", type.DeclaredProperties.OrderBy(p => p.Name).Select(p => p.Name + ": " + p.Type.FullName()))
                + "; " + typeof(System.Data.Linq.Binary).Assembly.FullName, "text/plain");
        }
    }

    public class Document
    {
        public int Id { get; set; }

        public System.Data.Linq.Binary Blob { get; set; }

        public byte[] Bytes { get; set; }
    }
}
