<%@ Page Language="C#" %>
<%@ Import Namespace="System.Collections.Generic" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Reflection" %>
<script runat="server">
    // Issue #41 (Opserver): Assembly.GetTypes() on Microsoft's System.Data.SqlClient packages throws on .NET. Their .NET
    // build has SqlGuidCaster, an explicit-layout type the runtime refuses to load, so a scan of every loaded assembly
    // failed (Opserver finds its tabs that way). webForCore builds System.Data.SqlClient from Microsoft's source with
    // that type fixed: GetTypes() works on it, and a scan of every assembly the application has loaded, written as
    // Opserver writes it (no catch for ReflectionTypeLoadException), finds every type.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        var sqlClient = typeof(System.Data.SqlClient.SqlConnection).Assembly;

        try
        {
            var types = sqlClient.GetTypes();
            Response.Write("GetTypes: ok, has SqlConnection: " + types.Contains(typeof(System.Data.SqlClient.SqlConnection)) + "\n");
        }
        catch (ReflectionTypeLoadException ex)
        {
            var missing = ex.LoaderExceptions.OfType<TypeLoadException>().Select(l => l.TypeName).Distinct();
            Response.Write("GetTypes: ReflectionTypeLoadException, " + ex.Types.Count(t => t == null) + " not loaded: " +
                string.Join(", ", missing) + "\n");
        }

        var all = new List<Type>();
        var failures = new List<string>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try { all.AddRange(assembly.GetTypes()); }
            catch (ReflectionTypeLoadException ex)
            {
                failures.Add(assembly.GetName().Name + " (" + string.Join(", ",
                    ex.LoaderExceptions.OfType<TypeLoadException>().Select(l => l.TypeName).Distinct()) + ")");
            }
        }
        Response.Write("assemblies scanned: " + assemblies.Length + "\n");
        Response.Write("scan finds SqlConnection: " + all.Contains(typeof(System.Data.SqlClient.SqlConnection)) + "\n");
        Response.Write("scan finds this page: " + all.Contains(GetType()) + "\n");
        Response.Write("scan failures: " + failures.Count + "\n");
        foreach (var failure in failures) Response.Write("failed: " + failure + "\n");
    }
</script>
