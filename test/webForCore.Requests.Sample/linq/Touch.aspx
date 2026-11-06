<%@ Page Language="C#" %>
<script runat="server">
    // Changes a product's price through LINQ to SQL, as another user would, so that its rowversion changes:
    // LinqDataSourceTests' concurrency case.
    protected void Page_Load(object sender, EventArgs e)
    {
        using (var context = new LinqCatalogDataContext())
        {
            int id = int.Parse(Request.QueryString["id"]);
            LinqProduct product = context.Products.Single(p => p.ProductID == id);
            product.UnitPrice = decimal.Parse(Request.QueryString["price"], System.Globalization.CultureInfo.InvariantCulture);
            context.SubmitChanges();
            Response.Write("touched " + product.ProductName + " " + product.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
</script>
