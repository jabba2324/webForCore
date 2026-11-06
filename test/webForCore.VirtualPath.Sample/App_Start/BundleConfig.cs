using System.Web.Optimization;

namespace VirtualPathSample
{
    // As the Visual Studio templates' BundleConfig, with optimizations on: bundle URLs carry their version hash.
    public static class BundleConfig
    {
        public static void RegisterBundles(BundleCollection bundles)
        {
            bundles.Add(new ScriptBundle("~/bundles/site").Include("~/Scripts/site.js"));
            bundles.Add(new StyleBundle("~/Content/css").Include("~/Content/site.css"));
            BundleTable.EnableOptimizations = true;
        }
    }
}
