using System.Data.SqlClient;
using Microsoft.AspNetCore.Builder;

// The database, on the test SQL Server of tools/ci/sqlserver.sh (the CI jobs' server); Global.asax creates the SQL
// providers' tables in it.
public class Program
{
    public const string Server = "Data Source=127.0.0.1,14333;User ID=sa;Password=webForCore-Test1!;TrustServerCertificate=True";
    public const string Database = Server + ";Initial Catalog=webforcore_smoke_sql";

    public static void Main(string[] args)
    {
        Execute(Server, "IF DB_ID('webforcore_smoke_sql') IS NULL CREATE DATABASE webforcore_smoke_sql");
        Execute(Database, "IF OBJECT_ID('Items') IS NULL BEGIN CREATE TABLE Items (Id int PRIMARY KEY, Name nvarchar(20)); INSERT INTO Items VALUES (1, N'one'), (2, N'two') END");

        var app = WebApplication.CreateBuilder(args).Build();
        app.UseWebForms(o => o.HandleAllRequestsWithWebForms());
        app.Run();
    }

    public static void Execute(string connectionString, string sql)
    {
        using (var connection = new SqlConnection(connectionString))
        using (var command = new SqlCommand(sql, connection))
        {
            connection.Open();
            command.ExecuteNonQuery();
        }
    }
}
