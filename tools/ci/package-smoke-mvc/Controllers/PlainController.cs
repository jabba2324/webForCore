using System.Web.Mvc;

namespace SmokeMvc.Controllers
{
    // A Razor view without dependency injection: compiled with webForCore's Razor although Unity.Mvc brings
    // Microsoft.AspNet.Razor 3.2.7 (#23).
    public class PlainController : Controller
    {
        public ActionResult Index() => View((object)"plain");
    }
}
