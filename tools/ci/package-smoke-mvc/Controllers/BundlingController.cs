using System.Web.Mvc;
using System.Web.Optimization;
using BundleTransformer.Core.Bundles;
using BundleTransformer.Core.Transformers;

namespace SmokeMvc.Controllers
{
    // Issue #141: webForCore's System.Web.Optimization and WebGrease have Microsoft's identities (1.1.0.0 and
    // 1.6.5135.21930, PublicKeyToken 31bf3856ad364e35), so BundleTransformer, built against Microsoft's bundling,
    // compiles and binds against them. SmartStoreNET's bundles failed to build: CS0012 ("You must add a reference to
    // assembly 'System.Web.Optimization, Version=1.1.0.0, ... PublicKeyToken=31bf3856ad364e35'") and CS0039, against
    // webForCore's 11.0.0.0 with the other token.
    public class BundlingController : Controller
    {
        public ContentResult Index()
        {
            return Content(typeof(Bundle).Assembly.FullName
                + "; " + typeof(WebGrease.CssMinifier).Assembly.FullName
                + "; BundleTransformer's StyleTransformer is an IBundleTransform: " + typeof(IBundleTransform).IsAssignableFrom(typeof(StyleTransformer))
                + ", its CustomScriptBundle a Bundle: " + typeof(Bundle).IsAssignableFrom(typeof(CustomScriptBundle)),
                "text/plain");
        }

        // Compiled, not called (BundleTransformer reads its configuration section): SmartStoreNET's conversions.
        static IBundleTransform Transform()
        {
            return new StyleTransformer();
        }

        static Bundle ScriptBundle()
        {
            return (Bundle)new CustomScriptBundle("~/bundles/smoke");
        }
    }
}
