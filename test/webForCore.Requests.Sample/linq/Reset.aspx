<%@ Page Language="C#" %>
<%@ Import Namespace="System.Data.SqlClient" %>
<script runat="server">
    // Recreates the LINQ to SQL gate's database (Web.config's LinqCatalog) with its rows, for LinqDataSourceTests.
    protected void Page_Load(object sender, EventArgs e)
    {
        var builder = new SqlConnectionStringBuilder(ConfigurationManager.ConnectionStrings["LinqCatalog"].ConnectionString);
        string database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        using (var connection = new SqlConnection(builder.ConnectionString))
        {
            connection.Open();
            Execute(connection, "IF DB_ID('" + database + "') IS NOT NULL BEGIN ALTER DATABASE [" + database + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + database + "] END");
            Execute(connection, "CREATE DATABASE [" + database + "]");
        }
        SqlConnection.ClearAllPools();
        using (var context = new LinqCatalogDataContext())
        {
            context.ExecuteCommand(
                "CREATE TABLE dbo.Categories (CategoryID int IDENTITY PRIMARY KEY, CategoryName nvarchar(50) NOT NULL);" +
                "CREATE TABLE dbo.Products (ProductID int IDENTITY PRIMARY KEY, ProductName nvarchar(50) NOT NULL," +
                " CategoryID int NOT NULL REFERENCES dbo.Categories, UnitPrice money NOT NULL, Discontinued bit NOT NULL, Version rowversion NOT NULL);" +
                "INSERT dbo.Categories (CategoryName) VALUES (N'Beverages'), (N'Condiments'), (N'Seafood');" +
                "INSERT dbo.Products (ProductName, CategoryID, UnitPrice, Discontinued) VALUES" +
                " (N'Chai', 1, 18, 0), (N'Chang', 1, 19, 0), (N'Aniseed Syrup', 2, 10, 0), (N'Ikura', 3, 31, 0)," +
                " (N'Konbu', 3, 6, 1), (N'Tofu', 2, 23.25, 0), (N'Pavlova', 2, 17.45, 1);");
            Response.Write("reset " + context.Products.Count());
        }
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using (var command = new SqlCommand(sql, connection)) command.ExecuteNonQuery();
    }
</script>
