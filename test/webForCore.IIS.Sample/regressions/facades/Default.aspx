<%@ Page Language="C#" %>
<script runat="server">
    // Issue #20: System.Web.Abstractions and System.Web.Routing are listed in this folder's web.config, as in MVC 2-4
    // projects' (Roadkill), and .NET 3.5 libraries reference them. On .NET Framework 4 they forward to System.Web.
    protected void Page_Load(object sender, EventArgs e)
    {
        var context = Type.GetType("System.Web.HttpContextBase, System.Web.Abstractions, Version=3.5.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35", true);
        var routes = Type.GetType("System.Web.Routing.RouteTable, System.Web.Routing, Version=3.5.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35", true);
        HttpContextBase wrapped = new HttpContextWrapper(Context);
        Response.ContentType = "text/plain";
        Response.Write("HttpContextBase: " + (context == typeof(HttpContextBase)) + "\n");
        Response.Write("RouteTable: " + (routes == typeof(System.Web.Routing.RouteTable)) + "\n");
        Response.Write("path: " + wrapped.Request.AppRelativeCurrentExecutionFilePath + "\n");
    }
</script>
