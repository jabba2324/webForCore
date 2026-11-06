<%@ Page Language="C#" %>
<script runat="server">
    // LinqDataSource with AutoGenerateWhereClause (a Where from the parameters' names), an OrderBy with a parameter-free
    // direction, a Select projection, and a Selecting handler that replaces the query with its own (?custom=1).
    protected void Source_Selecting(object sender, LinqDataSourceSelectEventArgs e)
    {
        Log.Text += "selecting category=" + e.WhereParameters["CategoryID"] + ";";
        if (Request.QueryString["custom"] == "1")
        {
            var context = new LinqCatalogDataContext();
            e.Result = context.Products.Where(p => p.Discontinued).OrderBy(p => p.ProductName).Select(p => new { p.ProductName, p.UnitPrice });
        }
    }
</script>
<!DOCTYPE html>
<html>
<head><title>LinqDataSource</title></head>
<body>
<form id="form1" runat="server">
    <asp:LinqDataSource ID="Source" runat="server" ContextTypeName="LinqCatalogDataContext" TableName="Products"
        AutoGenerateWhereClause="true" OrderBy="UnitPrice desc" Select="new (ProductName, UnitPrice)" OnSelecting="Source_Selecting">
        <WhereParameters>
            <asp:QueryStringParameter Name="CategoryID" QueryStringField="category" Type="Int32" />
        </WhereParameters>
    </asp:LinqDataSource>
    <asp:GridView ID="Grid" runat="server" DataSourceID="Source" AutoGenerateColumns="true" />
    <p>Log: <asp:Label ID="Log" runat="server" EnableViewState="false" /></p>
</form>
</body>
</html>
