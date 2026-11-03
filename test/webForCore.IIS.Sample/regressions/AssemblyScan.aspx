<%@ Page Language="C#" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Reflection" %>
<script runat="server">
    // Issue #41 (not fixed, documented in docs/MIGRATING.md): Assembly.GetTypes() on System.Data.SqlClient throws on .NET.
    // Its .NET build has SqlGuidCaster, an explicit-layout type the runtime refuses to load, so a scan of every loaded
    // assembly fails (Opserver finds its tabs that way). This page pins both the difference and the documented
    // workaround: catch ReflectionTypeLoadException and use the types that did load.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        var sqlClient = typeof(System.Data.SqlClient.SqlConnection).Assembly;

        try
        {
            sqlClient.GetTypes();
            Response.Write("GetTypes: ok\n");
        }
        catch (ReflectionTypeLoadException ex)
        {
            var missing = ex.LoaderExceptions.OfType<TypeLoadException>().Select(l => l.TypeName).Distinct();
            Response.Write("GetTypes: ReflectionTypeLoadException, " + ex.Types.Count(t => t == null) + " not loaded: " +
                string.Join(", ", missing) + "\n");
        }

        Response.Write("GetExportedTypes has SqlConnection: " +
            sqlClient.GetExportedTypes().Contains(typeof(System.Data.SqlClient.SqlConnection)) + "\n");

        // The workaround from docs/MIGRATING.md, as an application would write it.
        var failures = 0;
        var all = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a =>
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
            catch (Exception) { failures++; return Type.EmptyTypes; }
        }).ToList();
        Response.Write("scan finds SqlConnection: " + all.Contains(typeof(System.Data.SqlClient.SqlConnection)) + "\n");
        Response.Write("scan finds this page: " + all.Contains(GetType()) + "\n");
        Response.Write("scan failures: " + failures + "\n");
    }
</script>
