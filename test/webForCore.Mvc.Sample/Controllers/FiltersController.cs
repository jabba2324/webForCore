using System;
using System.Web.Helpers;
using System.Web.Mvc;

namespace MvcSample.Controllers
{
    // Action filters, authorization, verb selection, anti-forgery and errors.
    [Stamp("controller")]
    public class FiltersController : Controller
    {
        [Stamp("action")]
        public ActionResult Stamped()
        {
            return Content("stamped");
        }

        [Authorize]
        public ActionResult Secret()
        {
            return Content("secret");
        }

        [HttpPost]
        public ActionResult PostOnly()
        {
            return Content("posted");
        }

        [ActionName("renamed-action")]
        public ActionResult Renamed()
        {
            return Content("renamed");
        }

        public ActionResult Tokens()
        {
            AntiForgery.GetTokens(null, out string cookieToken, out string formToken);
            return Json(new { cookieToken, formToken }, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Protected()
        {
            return Content("validated");
        }

        public ActionResult Throws()
        {
            throw new InvalidOperationException("Boom");
        }

        public ActionResult Status(int code)
        {
            return new HttpStatusCodeResult(code, "Custom description");
        }

        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            Response.AppendHeader("X-Controller-OnActionExecuting", filterContext.ActionDescriptor.ActionName);
        }
    }

    // Adds a header before the action and appends to a ContentResult after it.
    public class StampAttribute : ActionFilterAttribute
    {
        private readonly string _name;

        public StampAttribute(string name)
        {
            _name = name;
        }

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            filterContext.HttpContext.Response.AppendHeader("X-Stamp-" + _name, "before");
        }

        public override void OnResultExecuting(ResultExecutingContext filterContext)
        {
            if (filterContext.Result is ContentResult content)
            {
                content.Content += "+" + _name;
            }
        }
    }
}
