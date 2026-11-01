using System.Web.Mvc;
using Microsoft.AspNet.SignalR;

namespace SmokeMvc.Controllers
{
    // Issue #40: webForCore's SignalR and System.Web.DataVisualization have Microsoft's assembly versions (2.4.3.0,
    // 4.0.0.0), so libraries built against Microsoft's compile and bind: SignalR's SQL Server scale-out (Microsoft's
    // package, as JabbR has it) was CS1705 against SignalR.Core 0.0.0.0.
    public class VersionsController : Controller
    {
        public ContentResult Index()
        {
            return Content("SignalR " + typeof(Hub).Assembly.GetName().Version
                + ", scale-out " + typeof(SqlScaleoutConfiguration).BaseType.Assembly.GetName().Version
                + ", DataVisualization " + typeof(System.Web.UI.DataVisualization.Charting.Chart).Assembly.GetName().Version,
                "text/plain");
        }

        // Compiled, not called (it needs a database): Microsoft's scale-out package against webForCore's SignalR.
        static void ScaleOut()
        {
            GlobalHost.DependencyResolver.UseSqlServer("Data Source=unused");
        }
    }
}
