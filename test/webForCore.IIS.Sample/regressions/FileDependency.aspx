<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Threading" %>
<script runat="server">
    // Issue #2: CacheDependency on one file under two spellings (bundling does this: the file's path and the bundle's
    // virtual path dependency) threw "Item has already been added" on Linux; eShopLegacyMVC's @Styles.Render did.
    // And a change to the file must reach both dependencies, as FileChangesMonitor delivers it on Windows.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string folder = Server.MapPath("~/App_Data/regressions");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "dependency-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, "before");
        try
        {
            var first = new System.Web.Caching.CacheDependency(file);
            var second = new System.Web.Caching.CacheDependency(Path.Combine(folder, ".", Path.GetFileName(file)));
            Response.Write("two dependencies: ok\n");

            File.WriteAllText(file, "after");
            for (int i = 0; i < 100 && !(first.HasChanged && second.HasChanged); i++) Thread.Sleep(50);
            Response.Write("first changed: " + first.HasChanged + "\n");
            Response.Write("second changed: " + second.HasChanged + "\n");
            first.Dispose();
            second.Dispose();
        }
        finally
        {
            File.Delete(file);
        }
    }
</script>
