using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using MvcSample.Models;

namespace MvcSample.Controllers
{
    // Model binding, validation, JSON and redirects (post/redirect/get with TempData).
    public class ProductsController : Controller
    {
        private static readonly List<Product> Products = new List<Product>
        {
            new Product { Id = 1, Name = "Tomato Soup", Price = 1 },
            new Product { Id = 2, Name = "Yo-yo", Price = 3.75M, Tags = new List<string> { "toy", "classic" },
                          Supplier = new Address { City = "Oslo", Country = "NO" } },
        };

        public ActionResult Index()
        {
            return Json(Products.Select(p => new { p.Id, p.Name, p.Price }), JsonRequestBehavior.AllowGet);
        }

        public ActionResult Details(int id)
        {
            Product product = Products.FirstOrDefault(p => p.Id == id);
            if (product == null)
            {
                return HttpNotFound();
            }
            ViewBag.Saved = TempData["saved"];
            return View(product);
        }

        public ActionResult Edit(int id)
        {
            Product product = Products.FirstOrDefault(p => p.Id == id);
            return product == null ? (ActionResult)HttpNotFound() : View(product);
        }

        [HttpPost]
        public ActionResult Create(Product product)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Json(ModelState.Where(m => m.Value.Errors.Count > 0)
                    .ToDictionary(m => m.Key, m => m.Value.Errors.Select(e => e.ErrorMessage).ToArray()));
            }
            product.Id = 2;
            TempData["saved"] = product.Name;
            return RedirectToAction("Details", new { id = product.Id });
        }

        [HttpPost]
        public ActionResult Echo(Product product)
        {
            return Json(new
            {
                product.Name,
                product.Price,
                product.Tags,
                City = product.Supplier?.City,
                Country = product.Supplier?.Country,
            });
        }

        public ActionResult Sum(int[] values, int offset = 0)
        {
            return Content((values ?? new int[0]).Sum() + offset + "", "text/plain");
        }
    }
}
