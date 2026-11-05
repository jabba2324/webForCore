<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.Management" %>
<script runat="server">
    // Issue #128: Server.MachineName called kernel32's GetComputerName, which threw DllNotFoundException off Windows;
    // ELMAH (AspNetDocs' Wingtip Toys) reads it for every error it logs and on /elmah.axd/rss and /elmah.axd/json.
    // On .NET Framework it is GetComputerName's name, which is what Environment.MachineName returns there too, and what
    // health monitoring reports (WebApplicationInformation.MachineName).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("server: " + Server.MachineName + "\n");
        Response.Write("wrapper: " + new HttpServerUtilityWrapper(Server).MachineName + "\n");
        Response.Write("environment: " + Environment.MachineName + "\n");
        Response.Write("health monitoring: " + WebBaseEvent.ApplicationInformation.MachineName + "\n");
    }
</script>
