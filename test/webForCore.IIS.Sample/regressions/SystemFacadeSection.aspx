<%@ Page Language="C#" %>
<script runat="server">
    // Issue #19: .NET Framework libraries reach some System.Configuration types through System.dll (ELMAH's section
    // handlers derive from SingleTagSectionHandler in System, Version=2.0.0.0). On .NET, System.dll is a facade that
    // forwards them to System.Configuration.ConfigurationManager; they must be the application's types, as on IIS.
    protected void Page_Load(object sender, EventArgs e)
    {
        var viaFacade = Type.GetType("System.Configuration.IConfigurationSectionHandler, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", true);
        Response.ContentType = "text/plain";
        Response.Write("same type: " + (viaFacade == typeof(System.Configuration.IConfigurationSectionHandler)) + "\n");
        var section = (System.Collections.IDictionary)System.Configuration.ConfigurationManager.GetSection("regressionsSingleTag");
        Response.Write("section: " + (section == null ? "null" : section["probe"]) + "\n");
    }
</script>
