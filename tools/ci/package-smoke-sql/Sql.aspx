<%@ Page Language="C#" %>
<%@ Import Namespace="System.Data" %>
<%@ Import Namespace="System.Data.Common" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Reflection" %>
<%@ Import Namespace="System.Web.Security" %>
<%@ Import Namespace="System.Web.Profile" %>
<%@ Import Namespace="System.Web.Caching" %>
<%-- #195: webForCore's SQL Server features on the System.Data.SqlClient in the build, one line each. --%>
<%
    Response.ContentType = "text/plain";
    string cs = Program.Database;
    Assembly sqlClient = typeof(System.Data.SqlClient.SqlConnection).Assembly;
    Response.Write("System.Data.SqlClient: " + sqlClient.GetName().Version + ", file " + System.Diagnostics.FileVersionInfo.GetVersionInfo(sqlClient.Location).FileVersion + "\n");
    string getTypes;
    try { sqlClient.GetTypes(); getTypes = "ok"; }
    catch (ReflectionTypeLoadException) { getTypes = "ReflectionTypeLoadException"; }
    Response.Write("GetTypes: " + getTypes + "\n");
    // System.Web was built against webForCore's: it binds to whichever is in the build.
    Response.Write("System.Web binds: " + (typeof(SqlDataSource).Assembly.GetReferencedAssemblies().Any(a => a.Name == "System.Data.SqlClient" && Assembly.Load(a) == sqlClient)) + "\n");
    Response.Write("DbProviderFactories: " + (DbProviderFactories.GetFactory("System.Data.SqlClient").GetType().Assembly == sqlClient) + "\n");

    var source = new SqlDataSource(cs, "SELECT Name FROM Items ORDER BY Id");
    Response.Write("SqlDataSource: " + string.Join(",", ((DataView)source.Select(DataSourceSelectArguments.Empty)).Cast<DataRowView>().Select(r => (string)r["Name"])) + "\n");

    Membership.DeleteUser("smoke", true);
    MembershipCreateStatus status;
    Membership.CreateUser("smoke", "Smoke-Pass1!", "smoke@example.org", "question", "answer", true, out status);
    Response.Write("membership: " + status + ", " + Membership.ValidateUser("smoke", "Smoke-Pass1!") + "\n");
    if (!Roles.RoleExists("Smokers")) Roles.CreateRole("Smokers");
    Roles.AddUserToRole("smoke", "Smokers");
    Response.Write("roles: " + Roles.IsUserInRole("smoke", "Smokers") + "\n");
    ProfileBase profile = ProfileBase.Create("smoke", true);
    profile["Color"] = "green";
    profile.Save();
    Response.Write("profile: " + ProfileBase.Create("smoke", true)["Color"] + "\n");

    using (var context = new System.Data.Linq.DataContext(cs))
        Response.Write("LINQ to SQL: " + context.ExecuteQuery<int>("SELECT COUNT(*) FROM Items").Single() + "\n");

    // Polling SqlCacheDependency (pollTime 500 ms): a change to the table removes the cache entry.
    Cache.Insert("smoke", "cached", new SqlCacheDependency("smoke", "Items"));
    using (var connection = new System.Data.SqlClient.SqlConnection(cs))
    using (var command = new System.Data.SqlClient.SqlCommand("UPDATE Items SET Name = Name WHERE Id = 1", connection))
    {
        connection.Open();
        command.ExecuteNonQuery();
    }
    var waited = System.Diagnostics.Stopwatch.StartNew();
    while (Cache["smoke"] != null && waited.Elapsed.TotalSeconds < 10) System.Threading.Thread.Sleep(100);
    Response.Write("SqlCacheDependency: " + (Cache["smoke"] == null ? "removed" : "still cached") + "\n");

    using (var connection = new Microsoft.Data.SqlClient.SqlConnection(cs))
    using (var command = new Microsoft.Data.SqlClient.SqlCommand("SELECT COUNT(*) FROM Items", connection))
    {
        connection.Open();
        Response.Write("Microsoft.Data.SqlClient " + typeof(Microsoft.Data.SqlClient.SqlConnection).Assembly.GetName().Version.Major + ": " + command.ExecuteScalar() + "\n");
    }
    var mds = new SqlDataSource(cs, "SELECT COUNT(*) AS N FROM Items") { ProviderName = "Microsoft.Data.SqlClient" };
    Response.Write("SqlDataSource on Microsoft.Data.SqlClient: " + ((DataView)mds.Select(DataSourceSelectArguments.Empty))[0]["N"] + "\n");

    Type shared = Type.GetType("SharedData.Items, SharedData");
    Response.Write("shared library: " + (shared == null ? "(none)" : shared.GetMethod("Count").Invoke(null, new object[] { cs })) + "\n");
%>
