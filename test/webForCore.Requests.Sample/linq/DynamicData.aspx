<%@ Page Language="C#" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Web.DynamicData" %>
<script runat="server">
    // Dynamic Data's LINQ to SQL model provider: MetaModel.RegisterContext with the DataContext type, as a Dynamic
    // Data site's Global.asax does it, and the tables and columns the provider reads from LINQ to SQL's mapping.
    private static readonly Lazy<MetaModel> Model = new Lazy<MetaModel>(() =>
    {
        var model = new MetaModel();
        model.RegisterContext(typeof(LinqCatalogDataContext), new ContextConfiguration { ScaffoldAllTables = true });
        return model;
    });

    protected void Page_Load(object sender, EventArgs e)
    {
        var text = new System.Text.StringBuilder();
        foreach (MetaTable table in Model.Value.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            text.Append(table.Name + " (" + table.DataContextPropertyName + ", " + table.EntityType.Name + ", " + table.DisplayName + "):");
            foreach (MetaColumn column in table.Columns)
            {
                text.Append(" " + column.Name + " " + column.ColumnType.Name + (column.IsPrimaryKey ? " key" : "") + (column.IsGenerated ? " generated" : "")
                    + (column.IsReadOnly ? " readonly" : "") + (column.IsRequired ? " required" : "") + (column.MaxLength > 0 ? " max=" + column.MaxLength : "")
                    + (column.Scaffold ? "" : " hidden") + ";");
            }
            text.Append("\n");
        }
        MetaTable products = Model.Value.GetTable("Products");
        using (var context = (LinqCatalogDataContext)products.CreateContext())
        {
            text.Append("query: " + string.Join(",", ((IQueryable<LinqProduct>)products.GetQuery(context)).OrderBy(p => p.ProductID).Take(2).Select(p => p.ProductName)) + "\n");
        }
        Output.Text = HttpUtility.HtmlEncode(text.ToString());
    }
</script>
<!DOCTYPE html>
<html>
<head><title>Dynamic Data</title></head>
<body>
<form id="form1" runat="server">
    <pre><asp:Literal ID="Output" runat="server" /></pre>
</form>
</body>
</html>
