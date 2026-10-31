<%@ Page Language="C#" %>
<%@ Import Namespace="System.Data.SqlClient" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="CatalogDataTableAdapters" %>
<script runat="server">
    // A Visual Studio DataSet in App_Code (CatalogData.xsd): its typed DataSet and TableAdapters are generated when the
    // site compiles, as on IIS. ?reset=1 recreates the database and runs every kind of query once.
    protected void Page_Load(object sender, EventArgs e)
    {
        if (Request.QueryString["reset"] != "1")
            return;
        Reset();
        var results = new System.Text.StringBuilder();
        var products = new ProductsTableAdapter();

        // Typed rows, LINQ over a typed table, a parameterized query, a scalar query, a stored procedure's output.
        CatalogData.ProductsDataTable all = products.GetProducts();
        results.Append("linq=" + string.Join(",", all.Where(p => !p.IsUnitPriceNull() && p.UnitPrice > 15).Select(p => p.ProductName)) + ";");
        results.Append("bycategory=" + products.GetProductsByCategoryID(2)[0].ProductName + ";");
        results.Append("count=" + products.CountByCategory(1) + ";");
        int? highest = 0;
        results.Append("proc=" + new QueriesTableAdapter().ProductCount(false, ref highest) + "/" + highest + ";");

        // DB-direct methods, with optimistic concurrency: the second update finds the row changed.
        results.Append("insert=" + products.Insert("Tofu", 2, 23.25m, false) + ";");
        int tofu = products.GetProducts().First(p => p.ProductName == "Tofu").ProductID;
        results.Append("update=" + products.Update("Tofu 2", 2, 24m, false, tofu, "Tofu", 2, 23.25m, false) + ";");
        results.Append("stale=" + products.Update("Tofu 3", 2, 24m, false, tofu, "Tofu", 2, 23.25m, false) + ";");
        results.Append("delete=" + products.Delete(tofu, "Tofu 2", 2, 24m, false) + ";");

        // ObjectDataSource finds the TableAdapter's Insert by its parameters' names.
        InsertSource.Insert();
        results.Append("ods=" + products.GetProductsByCategoryID(2).Count(p => p.ProductName == "Ikura") + ";");

        // TableAdapterManager: a new category and its product, a changed and a deleted product, in one transaction.
        var categories = new CategoriesTableAdapter();
        var data = new CatalogData();
        categories.Fill(data.Categories);
        products.Fill(data.Products);
        CatalogData.CategoriesRow produce = data.Categories.AddCategoriesRow("Produce", "Fruit");
        data.Products.AddProductsRow("Apple", produce, 1.5m, false);
        data.Products.FindByProductID(1).UnitPrice = 20m;
        data.Products.First(p => p.ProductName == "Mystery").Delete();
        var manager = new TableAdapterManager { CategoriesTableAdapter = categories, ProductsTableAdapter = products };
        results.Append("updateall=" + manager.UpdateAll(data) + "/" + data.HasChanges() + ";");
        results.Append("apple=" + products.GetProducts().First(p => p.ProductName == "Apple").CategoryID + "/" + produce.CategoryID + ";");
        results.Append("children=" + data.Categories.FindByCategoryID(1).GetProductsRows().Length + ";");
        Results.Text = results.ToString();
        Grid.DataBind();
    }

    private static void Reset()
    {
        var builder = new SqlConnectionStringBuilder(ConfigurationManager.ConnectionStrings["CatalogConnectionString"].ConnectionString);
        string database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        using (var connection = new SqlConnection(builder.ConnectionString))
        {
            connection.Open();
            Execute(connection, "IF DB_ID('" + database + "') IS NOT NULL BEGIN ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "] END");
            Execute(connection, "CREATE DATABASE [" + database + "]");
            connection.ChangeDatabase(database);
            Execute(connection, "CREATE TABLE Categories (CategoryID int IDENTITY PRIMARY KEY, CategoryName nvarchar(15) NOT NULL, Description ntext NULL)");
            Execute(connection, "CREATE TABLE Products (ProductID int IDENTITY PRIMARY KEY, ProductName nvarchar(40) NOT NULL, CategoryID int NULL REFERENCES Categories, UnitPrice money NULL, Discontinued bit NOT NULL)");
            Execute(connection, "CREATE PROCEDURE ProductCount @Discontinued bit, @Highest int OUTPUT AS BEGIN SELECT @Highest = MAX(ProductID) FROM Products; SELECT COUNT(*) FROM Products WHERE Discontinued = @Discontinued END");
            Execute(connection, "INSERT Categories (CategoryName, Description) VALUES ('Beverages', 'Drinks'), ('Condiments', NULL)");
            Execute(connection, "INSERT Products (ProductName, CategoryID, UnitPrice, Discontinued) VALUES ('Chai', 1, 18, 0), ('Chang', 1, 19, 0), ('Aniseed Syrup', 2, 10, 0), ('Mystery', NULL, NULL, 1)");
        }
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using (var command = new SqlCommand(sql, connection))
            command.ExecuteNonQuery();
    }
</script>
<!DOCTYPE html>
<html>
<body>
    <form id="form1" runat="server">
        <asp:Label ID="Results" runat="server" />
        <asp:GridView ID="Grid" runat="server" DataSourceID="ProductsSource" AutoGenerateColumns="false" DataKeyNames="ProductID">
            <Columns>
                <asp:BoundField DataField="ProductName" HeaderText="Product" />
                <asp:BoundField DataField="UnitPrice" HeaderText="Price" DataFormatString="{0:0.00}" />
            </Columns>
        </asp:GridView>
        <asp:ObjectDataSource ID="ProductsSource" runat="server" TypeName="CatalogDataTableAdapters.ProductsTableAdapter"
            SelectMethod="GetProductsByCategoryID">
            <SelectParameters><asp:Parameter Name="CategoryID" Type="Int32" DefaultValue="1" /></SelectParameters>
        </asp:ObjectDataSource>
        <asp:ObjectDataSource ID="InsertSource" runat="server" TypeName="CatalogDataTableAdapters.ProductsTableAdapter"
            SelectMethod="GetProducts" InsertMethod="Insert">
            <InsertParameters>
                <asp:Parameter Name="ProductName" Type="String" DefaultValue="Ikura" />
                <asp:Parameter Name="CategoryID" Type="Int32" DefaultValue="2" />
                <asp:Parameter Name="UnitPrice" Type="Decimal" DefaultValue="31" />
                <asp:Parameter Name="Discontinued" Type="Boolean" DefaultValue="false" />
            </InsertParameters>
        </asp:ObjectDataSource>
    </form>
</body>
</html>
