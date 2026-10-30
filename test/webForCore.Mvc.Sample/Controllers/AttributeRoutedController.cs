using System.Web.Mvc;

namespace MvcSample.Controllers
{
    [RoutePrefix("greet")]
    public class AttributeRoutedController : Controller
    {
        [Route("{name:alpha}")]
        public ActionResult Hello(string name)
        {
            return Content("Hello " + name);
        }

        [Route("~/numbers/{n:int:min(10)}")]
        public ActionResult Number(int n)
        {
            return Content("n=" + n);
        }
    }
}
