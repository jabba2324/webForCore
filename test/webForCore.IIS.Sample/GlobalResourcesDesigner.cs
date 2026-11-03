using System.Globalization;
using System.Reflection;
using System.Resources;

namespace IISSample
{
    // RegressionTests (#33): what Visual Studio generates for App_GlobalResources/Gate.resx in a web application project
    // (its Designer.cs), compiled into the application's bin assembly. It loads the global resources that System.Web
    // compiles at run time by their fixed name, as mojoPortal's resource classes do. Global.asax reads it in
    // Application_Start and regressions/FixedAssemblyNames.aspx on a page.
    public static class GlobalResourcesDesigner
    {
        private static ResourceManager s_resourceManager;

        public static ResourceManager ResourceManager
        {
            get
            {
                if (s_resourceManager == null)
                    s_resourceManager = new ResourceManager("Resources.Gate", Assembly.Load("App_GlobalResources"));
                return s_resourceManager;
            }
        }

        public static string Greeting(CultureInfo culture) => ResourceManager.GetString("Greeting", culture);
    }
}
