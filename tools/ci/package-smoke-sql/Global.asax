<%@ Application Language="C#" %>
<%@ Import Namespace="System.Web.Caching" %>
<%@ Import Namespace="System.Web.Management" %>
<script runat="server">
    // The SQL providers' tables, as aspnet_regsql (webforcore-regsql, which runs the same SqlServices) creates them, and
    // the change notifications of SqlCacheDependency's polling.
    void Application_Start(object sender, EventArgs e)
    {
        SqlServices.Install("webforcore_smoke_sql", SqlFeatures.Membership | SqlFeatures.RoleManager | SqlFeatures.Profile, Program.Database);
        SqlServices.InstallSessionState("webforcore_smoke_sql", SessionStateType.Custom, Program.Database);
        SqlCacheDependencyAdmin.EnableNotifications(Program.Database);
        SqlCacheDependencyAdmin.EnableTableForNotifications(Program.Database, "Items");
    }
</script>
