using System;
using System.Web.Mvc;
using Microsoft.Web.Mvc;

namespace MvcSample.Controllers
{
    // Microsoft.Web.Mvc (MVC Futures) on the fork's MVC.
    public class FuturesController : Controller
    {
        // ActionMethodSelectorAttribute: only for X-Requested-With: XMLHttpRequest.
        [AjaxOnly]
        public ActionResult AjaxOnly()
        {
            return Content("ajax");
        }

        // ActionFilterAttribute setting the response content type.
        [ContentType("text/csv")]
        public ActionResult Csv()
        {
            return Content("a,b");
        }

        // Expression-based URL generation (MVC's routing and expression helpers).
        public ActionResult Link()
        {
            string about = LinkBuilder.BuildUrlFromExpression<HomeController>(Request.RequestContext, System.Web.Routing.RouteTable.Routes, c => c.About());
            string details = LinkBuilder.BuildUrlFromExpression<ProductsController>(Request.RequestContext, System.Web.Routing.RouteTable.Routes, c => c.Details(7));
            return Content(about + " " + details);
        }

        // Reflection-based model copy.
        public ActionResult Copy()
        {
            var to = new Models.Product();
            ModelCopier.CopyModel(new Models.Product { Id = 3, Name = "Copied", Price = 9 }, to);
            return Content(to.ToString());
        }

        // [SkipBinding] keeps the parameter out of model binding.
        public ActionResult Skip([SkipBinding] string name, string other)
        {
            return Content((name ?? "(null)") + "/" + other);
        }

        // Html.Serialize writes the state into the form; [Deserialize] reads it back on the next request.
        public ActionResult Wizard()
        {
            return View(new Models.WizardState
            {
                Step = 1,
                Name = "Ada",
                Choices = { "red", "green" },
                Started = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc),
                Shipping = new Models.Address { City = "Leeds", Country = "UK" },
            });
        }

        [HttpPost]
        public ActionResult Wizard([Deserialize] Models.WizardState state)
        {
            state.Step++;
            return Content($"step {state.Step}: {state.Name}, {string.Join("+", state.Choices)}, {state.Started:yyyy-MM-ddTHH:mm}Z, {state.Shipping.City}/{state.Shipping.Country}");
        }
    }
}
