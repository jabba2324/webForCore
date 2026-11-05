<%@ Page Language="C#" %>
<%@ Import Namespace="System.Configuration" %>
<%@ Import Namespace="System.Data.SqlClient" %>
<%@ Import Namespace="System.Web.Management" %>
<script runat="server">
    // Issue #123: webForCore had none of the scripts aspnet_regsql and SqlServices install ASP.NET's SQL providers with
    // (SqlServices.Install threw FileNotFoundException for InstallCommon.sql), so an application on the default
    // SqlMembershipProvider couldn't create its database: MVC Music Store's Register and LogOn were a 500. The sample
    // points LocalSqlServer at SQL Server (Web.config), and SqlServicesTests creates the database with webforcore-regsql.
    //   ?step=reset    drops the database
    //   ?step=members  Membership and Roles through machine.config's AspNetSql providers
    //   ?step=code     SqlServices from code: profile, and the tables session state (custom) added
    const string Database = "WebForCoreAspNetDb";

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string cs = ConfigurationManager.ConnectionStrings["LocalSqlServer"].ConnectionString;
        switch (Request.QueryString["step"])
        {
            case "reset":
                SqlConnection.ClearAllPools();
                var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
                Execute(master, "IF DB_ID('" + Database + "') IS NOT NULL BEGIN ALTER DATABASE [" + Database +
                    "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + Database + "] END");
                Response.Write("reset\n");
                break;
            case "members":
                Members();
                break;
            case "code":
                SqlServices.Install(Database, SqlFeatures.Profile, cs);
                Response.Write("tables: " + Scalar(cs, "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name) FROM sys.tables WHERE name IN " +
                    "('aspnet_Applications','aspnet_Membership','aspnet_Profile','aspnet_Roles','aspnet_Users','aspnet_UsersInRoles','ASPStateTempSessions')") + "\n");
                Response.Write("schema versions: " + Scalar(cs, "SELECT STRING_AGG(Feature, ',') WITHIN GROUP (ORDER BY Feature) FROM dbo.aspnet_SchemaVersions") + "\n");
                break;
        }
    }

    private void Members()
    {
        Response.Write("membership provider: " + Membership.Provider.GetType().FullName + "\n");
        Response.Write("role provider: " + Roles.Provider.GetType().FullName + "\n");
        string name = "user" + Guid.NewGuid().ToString("N").Substring(0, 8);
        MembershipCreateStatus status;
        MembershipUser user = Membership.CreateUser(name, "Passw0rd!", name + "@example.com", "Colour?", "Blue", true, out status);
        Response.Write("create: " + status + "\n");
        Response.Write("duplicate: " + Duplicate(name) + "\n");
        Response.Write("validate: " + Membership.ValidateUser(name, "Passw0rd!") + "\n");
        Response.Write("validate wrong password: " + Membership.ValidateUser(name, "wrong") + "\n");
        Response.Write("email: " + (Membership.GetUser(name).Email == name + "@example.com") + "\n");
        if (!Roles.RoleExists("Editors")) Roles.CreateRole("Editors");
        Roles.AddUserToRole(name, "Editors");
        Response.Write("in role: " + Roles.IsUserInRole(name, "Editors") + "\n");
        Response.Write("roles: " + string.Join(",", Roles.GetRolesForUser(name)) + "\n");
        Response.Write("delete: " + Membership.DeleteUser(name, true) + "\n");
        Response.Write("validate deleted: " + Membership.ValidateUser(name, "Passw0rd!") + "\n");
    }

    private static MembershipCreateStatus Duplicate(string name)
    {
        MembershipCreateStatus status;
        Membership.CreateUser(name, "Passw0rd!", "other@example.com", "Colour?", "Blue", true, out status);
        return status;
    }

    private static void Execute(string cs, string sql)
    {
        using (var connection = new SqlConnection(cs))
        using (var command = new SqlCommand(sql, connection))
        {
            connection.Open();
            command.ExecuteNonQuery();
        }
    }

    private static object Scalar(string cs, string sql)
    {
        using (var connection = new SqlConnection(cs))
        using (var command = new SqlCommand(sql, connection))
        {
            connection.Open();
            return command.ExecuteScalar();
        }
    }
</script>
