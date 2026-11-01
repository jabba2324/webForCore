<%@ Page Language="C#" %>
<script runat="server">
    // Issue #26: this folder's web.config has <appSettings configSource="Config\settings.config" />, a file in a
    // subfolder written with '\' as on Windows (MVCForum).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("setting: " + System.Configuration.ConfigurationManager.AppSettings["regressions:configsource"] + "\n");
    }
</script>
