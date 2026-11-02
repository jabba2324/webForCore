<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<script runat="server">
    // Issue #50: on IIS, HttpRuntime sets the AppDomain's "DataDirectory" to the site's App_Data, and data providers that
    // expand |DataDirectory| themselves (System.Data.SQLite, SqlClient's AttachDbFilename) read it there. Bonobo Git
    // Server's SQLite database was created in bin_dotnet/ instead.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        var data = AppDomain.CurrentDomain.GetData("DataDirectory") as string;
        Response.Write("DataDirectory: " + (data ?? "(null)") + "\n");
        Response.Write("is App_Data: " + (data != null && Path.GetFullPath(data) == Path.GetFullPath(Server.MapPath("~/App_Data"))) + "\n");
    }
</script>
