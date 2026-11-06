using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using CacheCow.Server.WebApi;
using PackagesSample.Services;

namespace PackagesSample.Controllers
{
    // CacheCow.Server.WebApi's filter: an ETag computed from the response on GET, 304 for a matching If-None-Match, and a
    // new ETag once a PUT or POST has changed the resource.
    public class ProductsController : ApiController
    {
        private readonly IProductStore _store;

        public ProductsController(IProductStore store) => _store = store;

        [HttpCache(DefaultExpirySeconds = 0)]
        public IEnumerable<Product> Get() => _store.All();

        [HttpCache(DefaultExpirySeconds = 0)]
        public IHttpActionResult Get(int id)
        {
            var product = _store.Get(id);
            return product == null ? (IHttpActionResult)NotFound() : Ok(product);
        }

        [HttpCache]
        public IHttpActionResult Put(int id, Product product)
        {
            if (_store.Get(id) == null) return NotFound();
            product.Id = id;
            return Ok(_store.Save(product));
        }

        [HttpCache]
        public IHttpActionResult Post(Product product)
        {
            product.Id = 0;
            var saved = _store.Save(product);
            return Content(HttpStatusCode.Created, saved);
        }
    }
}
