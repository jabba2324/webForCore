<%@ Page Language="C#" %>
<script runat="server">
    // Issue #35: .NET Framework libraries name the SqlClient types in System.Data (Microsoft.ApplicationBlocks.Data's
    // SqlHelper: Subtext). On .NET, System.Data.dll is a facade that forwards them to System.Data.SqlClient, which the
    // application deploys; they must be the application's types, as on IIS.
    protected void Page_Load(object sender, EventArgs e)
    {
        var viaFacade = Type.GetType("System.Data.SqlClient.SqlDataReader, System.Data, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        Response.ContentType = "text/plain";
        Response.Write("same type: " + (viaFacade == typeof(System.Data.SqlClient.SqlDataReader)) + "\n");
    }
</script>
