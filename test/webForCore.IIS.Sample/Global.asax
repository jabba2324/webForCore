<%@ Application Language="C#" %>
<script runat="server">
    void Application_Start(object sender, EventArgs e)
    {
        System.Threading.Interlocked.Increment(ref DomainState.ApplicationStarts);
        // System.Web.Optimization: bundling and minification, as in an MVC 5 / Web Forms template's BundleConfig.
        System.Web.Optimization.BundleTable.EnableOptimizations = true;
        System.Web.Optimization.BundleTable.Bundles.Add(new System.Web.Optimization.ScriptBundle("~/bundles/gate").Include("~/Scripts/one.js", "~/Scripts/two.js"));
        System.Web.Optimization.BundleTable.Bundles.Add(new System.Web.Optimization.StyleBundle("~/Content/gatecss").Include("~/Content/gate.css"));
        // RegressionTests (#13): a route for a URL that is also a directory, which RouteExistingFiles lets it take (an MVC
        // app whose controllers share their feature folders' names). No other route of the sample matches anything.
        System.Web.Routing.RouteTable.Routes.RouteExistingFiles = true;
        System.Web.Routing.RouteTable.Routes.MapPageRoute("RegressionsRouted", "regressions/routed", "~/regressions/Routed.aspx");
        // RegressionTests (#33): mojoPortal's Application_Start reads its global resources through Visual Studio's designer
        // class, which loads them with Assembly.Load("App_GlobalResources").
        try
        {
            DomainState.GlobalResourceAtStart = IISSample.GlobalResourcesDesigner.Greeting(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception x)
        {
            DomainState.GlobalResourceAtStart = x.GetType().Name + ": " + x.Message;
        }
    }

    // RegressionTests (#184): a flush after the output cache took its snapshot of the response's buffers (EndRequest
    // comes after UpdateRequestCache) sends and clears them; the cached copy must keep its bytes.
    void Application_EndRequest(object sender, EventArgs e)
    {
        if (Request.Path.EndsWith("/regressions/unbuffered/Cached.aspx", StringComparison.OrdinalIgnoreCase) && Request.QueryString["flush"] == "1")
        {
            Response.Flush();
        }
    }

    // RegressionTests (#184): regressions/unbuffered/Cached.aspx?provider=1 is cached by the application's own output
    // cache provider (Web.config's GateOutputCache).
    public override string GetOutputCacheProviderName(HttpContext context)
    {
        if (context.Request.Path.EndsWith("/regressions/unbuffered/Cached.aspx", StringComparison.OrdinalIgnoreCase) && context.Request.QueryString["provider"] == "1")
        {
            return "GateOutputCache";
        }
        return base.GetOutputCacheProviderName(context);
    }
</script>
