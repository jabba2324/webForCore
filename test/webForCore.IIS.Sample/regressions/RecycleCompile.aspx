<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Web.Compilation" %>
<script runat="server">
    // Issue #89: Umbraco 6.2.6's Application_Start creates ~/App_Code, which restarts the application. The next worker
    // finds the code directories changed and empties the compilation folder, which the workers share, while the old one
    // still compiles the page it was asked for: that page failed with CS0006, "Metadata file
    // '.../App_global.asax.xxxxxxxx.dll' could not be found". On IIS the new AppDomain couldn't delete the assemblies the
    // old one had loaded; it marked them .delete and left them for the next start (DiskBuildResultCache.TryDeleteFile).
    //
    // This page changes App_Code, waits until the next worker has cleaned the compilation folder (the test sends it a
    // request meanwhile), then compiles a page this worker hasn't compiled yet, as Umbraco's login.aspx was.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string global = Context.ApplicationInstance.GetType().Assembly.Location;
        Response.Write("global: " + Path.GetFileName(global) + "\n");
        File.SetLastWriteTimeUtc(Server.MapPath("~/App_Code/DomainState.cs"), DateTime.UtcNow);

        // The next worker has cleaned the folder when App_global.asax's assembly is gone (before #89) or marked .delete.
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (File.Exists(global) && !File.Exists(global + ".delete") && DateTime.UtcNow < deadline)
        {
            System.Threading.Thread.Sleep(50);
        }
        Response.Write("cleaned: " + (!File.Exists(global) ? "deleted" : File.Exists(global + ".delete") ? "marked" : "no") + "\n");

        string compiled;
        try
        {
            compiled = BuildManager.GetCompiledType("~/regressions/recycling/Compiled.aspx") != null ? "compiled" : "null";
        }
        catch (Exception x)
        {
            compiled = x.GetType().Name + ": " + x.Message;
        }
        Response.Write("page: " + compiled + "\n");
    }
</script>
