<%@ Page Language="C#" %>
<%@ Import Namespace="System.Globalization" %>
<%@ Import Namespace="System.Reflection" %>
<%@ Import Namespace="System.Resources" %>
<script runat="server">
    // Issue #33: Visual Studio's designer classes for App_GlobalResources load the global resources with
    // Assembly.Load("App_GlobalResources"). System.Web compiles them to a random name (App_GlobalResources.xxxxxxxx) and
    // answers AssemblyResolve for the fixed names, but on .NET the event gets the display name, so the lookup missed:
    // mojoPortal's Application_Start and every page failed with "Could not load file or assembly 'App_GlobalResources'".
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        foreach (string name in new[] { "App_Code", "App_GlobalResources", "__code" })
        {
            string loaded;
            try { loaded = Assembly.Load(name).GetName().Name; }
            catch (Exception x) { loaded = x.GetType().Name + ": " + x.Message; }
            Response.Write(name + ": " + loaded + "\n");
        }
        Response.Write("Application_Start: " + DomainState.GlobalResourceAtStart + "\n");
        string fr;
        try { fr = IISSample.GlobalResourcesDesigner.Greeting(new CultureInfo("fr")); }
        catch (Exception x) { fr = x.GetType().Name + ": " + x.Message; }
        Response.Write("fr: " + fr + "\n");
        Response.Write("GetGlobalResourceObject: " + GetGlobalResourceObject("Gate", "Greeting") + "\n");
    }
</script>
