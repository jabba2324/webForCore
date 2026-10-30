using System;
using System.Web.Script.Services;
using System.Web.Services;

namespace IISSample
{
    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string[] Tags { get; set; }
    }

    // An ASMX web service (services/Catalog.asmx): SOAP 1.1 and 1.2, HTTP POST from localhost, the help page and WSDL,
    // and JSON for ASP.NET AJAX ([ScriptService]).
    [WebService(Namespace = "http://example.com/catalog")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    [ScriptService]
    public class Catalog : WebService
    {
        [WebMethod]
        public int Add(int a, int b) => a + b;

        [WebMethod]
        public Item GetItem(int id) => new Item { Id = id, Name = "Item " + id, Price = 9.5m, Tags = new[] { "new", "sale" } };

        [WebMethod]
        public string Fail() => throw new InvalidOperationException("Out of stock");

        [WebMethod(EnableSession = true)]
        public int Visit()
        {
            int count = (Session["visits"] as int? ?? 0) + 1;
            Session["visits"] = count;
            return count;
        }
    }
}
