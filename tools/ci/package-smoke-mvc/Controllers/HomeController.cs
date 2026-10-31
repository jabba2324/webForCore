using System.Web.Mvc;

namespace SmokeMvc.Controllers
{
    // Constructed by Unity (#21); its view is Razor, compiled with webForCore's Razor although Unity.Mvc brings
    // Microsoft.AspNet.Razor 3.2.7 (#23).
    public class HomeController : Controller
    {
        private readonly IGreeter _greeter;

        public HomeController(IGreeter greeter)
        {
            _greeter = greeter;
        }

        public ActionResult Index()
        {
            ViewBag.PostStart = UnityMvcActivator.PostStartRan;
            return View((object)_greeter.Greet());
        }
    }
}
