<%@ Page Language="C#" %>
<%@ Import Namespace="System.Configuration" %>
<script runat="server">
    // Issue #154: Visual Studio's settings classes (Settings.settings, and the Url of every "Add Web Reference" SOAP
    // client) derive from ApplicationSettingsBase, which creates System.Configuration's ConfigurationManagerInternal by
    // name. The name pointed at System.Configuration 4.0.0.0, .NET's facade, which doesn't forward that internal type:
    // Umbraco 7.15's CheckForUpgrade web reference failed with "TypeLoadException: Could not resolve type
    // 'System.Configuration.Internal.ConfigurationManagerInternal'".

    // As Visual Studio generates them; the section names are the [SettingsGroupName]s (regressions/web.config).
    [SettingsGroupName("Regressions.Properties.Settings")]
    public sealed class AppScoped : ApplicationSettingsBase
    {
        [ApplicationScopedSetting, DefaultSettingValue("http://default.example/service.asmx"), SpecialSetting(SpecialSetting.WebServiceUrl)]
        public string ServiceUrl { get { return (string)this["ServiceUrl"]; } }

        [ApplicationScopedSetting, DefaultSettingValue("7")]
        public int Retries { get { return (int)this["Retries"]; } }

        [ApplicationScopedSetting, DefaultSettingValue("from the attribute")]
        public string NotInConfig { get { return (string)this["NotInConfig"]; } }
    }

    // A user-scoped setting: ASP.NET's configuration system doesn't support user settings, so on IIS reading any setting
    // of this class throws ConfigurationErrorsException (LocalFileSettingsProvider.GetPropertyValues).
    [SettingsGroupName("Regressions.Properties.UserSettings")]
    public sealed class UserScoped : ApplicationSettingsBase
    {
        [ApplicationScopedSetting, DefaultSettingValue("app default")]
        public string AppValue { get { return (string)this["AppValue"]; } }

        [UserScopedSetting, DefaultSettingValue("user default")]
        public string UserValue { get { return (string)this["UserValue"]; } set { this["UserValue"] = value; } }
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        var app = new AppScoped();
        Response.Write("ServiceUrl: " + Read(() => app.ServiceUrl) + "\n");
        Response.Write("Retries: " + Read(() => app.Retries) + "\n");
        Response.Write("NotInConfig: " + Read(() => app.NotInConfig) + "\n");
        var user = new UserScoped();
        Response.Write("user AppValue: " + Read(() => user.AppValue) + "\n");
        Response.Write("user UserValue: " + Read(() => user.UserValue) + "\n");
    }

    private static string Read(Func<object> read)
    {
        try { return Convert.ToString(read()); }
        catch (Exception x) { return x.GetType().FullName + ": " + x.Message; }
    }
</script>
