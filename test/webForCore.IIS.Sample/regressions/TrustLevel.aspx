<%@ Page Language="C#" %>
<%@ Import Namespace="System.Security" %>
<%@ Import Namespace="System.Web.Configuration" %>
<%@ Import Namespace="System.Web.Management" %>
<script runat="server">
    // Issue #87: the sample's Web.config sets <trust level="Medium" originUrl=".*" />, as Umbraco 6's development
    // web.config does. webForCore ignores trust levels (docs/NAMESPACES.md) and runs the application in full trust;
    // every request was a Configuration Error ("Unable to read the security policy file for trust level 'Medium'").
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        var trust = (TrustSection)WebConfigurationManager.GetSection("system.web/trust");
        Response.Write("configured: " + trust.Level + "\n");
        // What ASP.NET reports the application runs in: health monitoring's trust level and the named permission set,
        // which is null in full trust.
        Response.Write("runtime: " + WebBaseEvent.ApplicationInformation.TrustLevel + "\n");
        Response.Write("permission set: " + (HttpRuntime.GetNamedPermissionSet() == null ? "(none: full trust)" : HttpRuntime.GetNamedPermissionSet().Name) + "\n");
        // How applications detect their trust level (Umbraco's SystemUtilities.GetCurrentTrustLevel): the highest
        // AspNetHostingPermissionLevel whose demand succeeds.
        string detected = "None";
        foreach (var level in new[] { AspNetHostingPermissionLevel.Unrestricted, AspNetHostingPermissionLevel.High,
            AspNetHostingPermissionLevel.Medium, AspNetHostingPermissionLevel.Low, AspNetHostingPermissionLevel.Minimal })
        {
            try
            {
                new AspNetHostingPermission(level).Demand();
                detected = level.ToString();
                break;
            }
            catch (SecurityException)
            {
            }
        }
        Response.Write("demand: " + detected + "\n");
    }
</script>
