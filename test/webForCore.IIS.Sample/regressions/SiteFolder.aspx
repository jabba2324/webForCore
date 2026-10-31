<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<script runat="server">
    // Issue #3 (documented in docs/NAMESPACES.md and MIGRATING.md): AppDomain.CurrentDomain.BaseDirectory is the build
    // output, not the site root as in IIS's per-application AppDomain; HttpRuntime.AppDomainAppPath,
    // HostingEnvironment.ApplicationPhysicalPath and Server.MapPath("~/") are the site root, as on IIS.
    static string Folder(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("AppDomain.CurrentDomain.BaseDirectory: " + Folder(AppDomain.CurrentDomain.BaseDirectory) + "\n");
        Response.Write("HttpRuntime.AppDomainAppPath: " + Folder(HttpRuntime.AppDomainAppPath) + "\n");
        Response.Write("HostingEnvironment.ApplicationPhysicalPath: " + Folder(System.Web.Hosting.HostingEnvironment.ApplicationPhysicalPath) + "\n");
        Response.Write("Server.MapPath: " + Folder(Server.MapPath("~/")) + "\n");
    }
</script>
