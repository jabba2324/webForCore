<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.Compilation" %>
<script runat="server">
    // Issue #137 (DNN Platform 9, ClientDependency): BuildManager.GetType(typeName, throwOnError: false) threw
    // FileNotFoundException for an assembly-qualified name whose assembly isn't deployed. On .NET Framework it returns
    // null, as Type.GetType(typeName, false) does: ClientDependency's module skips its MVC filter that way
    // (BuildManager.GetType(type, false, true)), and DNN skips the providers of packages it hasn't installed.
    static string Describe(Func<Type> getType)
    {
        try
        {
            var type = getType();
            return type == null ? "null" : "found " + type.FullName;
        }
        catch (Exception ex)
        {
            return ex.GetType().FullName;
        }
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        // The repro of the issue, and ClientDependency's call.
        Response.Write("missing assembly: " + Describe(() => BuildManager.GetType("Missing.Type, Missing.Assembly", false)) + "\n");
        Response.Write("missing assembly, ignoreCase: " + Describe(() => BuildManager.GetType("ClientDependency.Core.Mvc.MvcFilter, ClientDependency.Core.Mvc", false, true)) + "\n");
        // A generic type argument whose assembly is missing.
        Response.Write("missing generic argument: " + Describe(() => BuildManager.GetType("System.Collections.Generic.List`1[[Missing.Type, Missing.Assembly]]", false)) + "\n");
        // A type that isn't in an assembly that is there.
        Response.Write("missing type: " + Describe(() => BuildManager.GetType("System.Web.NoSuchType, System.Web", false)) + "\n");
        // A type that is there, by assembly-qualified name and by name alone.
        Response.Write("present: " + Describe(() => BuildManager.GetType("System.Web.HttpContext, System.Web", false)) + "\n");
        Response.Write("present, ignoreCase: " + Describe(() => BuildManager.GetType("system.web.httpcontext, System.Web", false, true)) + "\n");
        // throwOnError: true still throws, with .NET Framework's exception types.
        Response.Write("missing assembly, throwOnError: " + Describe(() => BuildManager.GetType("Missing.Type, Missing.Assembly", true)) + "\n");
        Response.Write("missing type, throwOnError: " + Describe(() => BuildManager.GetType("System.Web.NoSuchType, System.Web", true)) + "\n");
        // Malformed names.
        Response.Write("malformed: " + Describe(() => BuildManager.GetType("Missing.Type, , ", false)) + "\n");
        Response.Write("malformed generic: " + Describe(() => BuildManager.GetType("Missing.Type[[, Missing.Assembly", false)) + "\n");
        // Type.GetType from the application's own code.
        Response.Write("Type.GetType missing assembly: " + Describe(() => Type.GetType("Missing.Type, Missing.Assembly", false)) + "\n");
        Response.Write("Type.GetType missing assembly, ignoreCase: " + Describe(() => Type.GetType("Missing.Type, Missing.Assembly", false, true)) + "\n");
        Response.Write("Type.GetType missing assembly, throwOnError: " + Describe(() => Type.GetType("Missing.Type, Missing.Assembly", true)) + "\n");
    }
</script>
