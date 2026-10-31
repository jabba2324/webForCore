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
    }
</script>
