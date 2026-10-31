<%@ Page Language="C#" %>
<script runat="server">
    // Issue #1: a .NET Framework library is compiled against System.Configuration, Version=4.0.0.0 (the shared
    // framework's facade, which forwards to System.Configuration.ConfigurationManager). Microsoft.AspNet.SessionState's
    // SessionStateModuleAsync and WebActivatorEx read configuration that way; it must be web.config, as on IIS.
    protected void Page_Load(object sender, EventArgs e)
    {
        var viaFacade = Type.GetType("System.Configuration.ConfigurationManager, System.Configuration, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", true);
        var appSettings = (System.Collections.Specialized.NameValueCollection)viaFacade.GetProperty("AppSettings").GetValue(null);
        var section = viaFacade.GetMethod("GetSection", new[] { typeof(string) }).Invoke(null, new object[] { "system.web/sessionState" });
        Response.ContentType = "text/plain";
        Response.Write("same type: " + (viaFacade == typeof(System.Configuration.ConfigurationManager)) + "\n");
        Response.Write("appSettings: " + appSettings["regressions:probe"] + "\n");
        Response.Write("sessionState: " + (section == null ? "null" : section.GetType().FullName) + "\n");
    }
</script>
