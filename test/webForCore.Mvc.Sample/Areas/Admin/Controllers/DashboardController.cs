using System.Web.Mvc;

namespace MvcSample.Areas.Admin.Controllers
{
    public class DashboardController : Controller
    {
        public ActionResult Index()
        {
            return Content("admin area; link=" + Url.Action("Index", "Home", new { area = "" }));
        }
    }
}
