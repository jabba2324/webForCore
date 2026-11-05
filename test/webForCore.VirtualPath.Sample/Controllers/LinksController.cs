using System.Collections.Generic;
using System.Web.Http;

namespace VirtualPathSample.Controllers
{
    // The URLs Web API generates in an application under a virtual path (#190): Url.Link (absolute) and Url.Route.
    public class LinksController : ApiController
    {
        public IDictionary<string, string> Get(int? id = null)
        {
            return new SortedDictionary<string, string>
            {
                ["id"] = id?.ToString() ?? "",
                ["link"] = Url.Link("DefaultApi", new { controller = "links", id = 5 }),
                ["route"] = Url.Route("DefaultApi", new { controller = "links", id = 6 }),
                ["content"] = Url.Content("~/Scripts/site.js"),
                ["virtual_path_root"] = RequestContext.VirtualPathRoot,
                ["scheme"] = Request.RequestUri.Scheme,
                ["path"] = Request.RequestUri.AbsolutePath,
            };
        }

        [Route("api/attribute/{name}")]
        public string GetAttribute(string name) => Url.Link("DefaultApi", new { controller = "links" }) + " " + name;
    }
}
