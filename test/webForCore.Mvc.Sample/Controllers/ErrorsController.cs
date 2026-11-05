using System;
using System.Web;
using System.Web.Mvc;

namespace MvcSample.Controllers
{
    // #192: HandleErrorAttribute (registered globally in FilterConfig) renders Views/Shared/Error.cshtml only when
    // customErrors is on (HttpContext.IsCustomErrorEnabled), and leaves an HttpException other than a 500 to
    // customErrors. Web.config turns customErrors on for /Errors and off for /ErrorsOff.
    public class ErrorsController : Controller
    {
        public ActionResult Throws()
        {
            throw new InvalidOperationException("Thrown by the action.");
        }

        public ActionResult NotFound()
        {
            throw new HttpException(404, "Not found by the action.");
        }

        public ActionResult Page()
        {
            return Content("error page, aspxerrorpath=" + Request.QueryString["aspxerrorpath"]);
        }
    }

    public class ErrorsOffController : ErrorsController
    {
    }
}
