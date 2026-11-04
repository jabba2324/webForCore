<%@ Page Language="C#" %>
<%@ Import Namespace="System.Reflection" %>
<%@ Import Namespace="System.Web.Compilation" %>
<script runat="server">
    // Issue #147 (Orchard 1.10): the <probing privatePath> of the application's Web.config wasn't applied. Orchard
    // copies each module's assembly to App_Data/Dependencies and loads it by name, so no module loaded. The sample's
    // Web.config names bin_dotnet/probed, where its build puts ProbedLib.dll (not referenced by the sample), and a
    // folder outside the site with UnprobedLib.dll, which fusion doesn't probe.
    static string Describe(Func<object> load)
    {
        try
        {
            var result = load();
            return result == null ? "null" : result.ToString();
        }
        catch (Exception ex)
        {
            return ex.GetType().FullName;
        }
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string root = HttpRuntime.AppDomainAppPath;
        Response.Write("Assembly.Load: " + Describe(() =>
        {
            var assembly = Assembly.Load("ProbedLib");
            return assembly.Location.Substring(root.Length).Replace('\\', '/');
        }) + "\n");
        Response.Write("Type.GetType: " + Describe(() => Type.GetType("ProbedLib.Greeting, ProbedLib", true).GetProperty("Text").GetValue(null)) + "\n");
        Response.Write("BuildManager.GetType: " + Describe(() => BuildManager.GetType("ProbedLib.Greeting, ProbedLib", true).FullName) + "\n");
        Response.Write("outside the site: " + Describe(() => Assembly.Load("UnprobedLib")) + "\n");
        Response.Write("outside the site, Type.GetType: " + Describe(() => Type.GetType("UnprobedLib.Outside, UnprobedLib", false)) + "\n");
    }
</script>
