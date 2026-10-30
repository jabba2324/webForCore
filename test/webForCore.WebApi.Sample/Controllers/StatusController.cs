using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace WebApiSample.Controllers
{
    // Attribute-routed actions for the other status-code paths of Web API 2.
    [RoutePrefix("api/status")]
    public class StatusController : ApiController
    {
        [Route("ok")]
        public IHttpActionResult GetOk() { return Ok(new { Message = "ok", Number = 42, Flag = true, When = new DateTime(2013, 9, 10, 19, 54, 49, DateTimeKind.Utc) }); }

        [Route("nocontent")]
        public IHttpActionResult GetNoContent() { return StatusCode(HttpStatusCode.NoContent); }

        [Route("redirect")]
        public IHttpActionResult GetRedirect() { return Redirect(new Uri("http://example.com/target")); }

        [Route("unauthorized")]
        public IHttpActionResult GetUnauthorized() { return Unauthorized(); }

        [Route("badrequest")]
        public IHttpActionResult GetBadRequest() { return BadRequest("Bad input"); }

        [Route("conflict")]
        public IHttpActionResult GetConflict() { return Conflict(); }

        [Route("httperror")]
        public HttpResponseMessage GetHttpError() { return Request.CreateErrorResponse(HttpStatusCode.Forbidden, "Not for you"); }

        [Route("throwsresponse")]
        public string GetThrowsResponse() { throw new HttpResponseException(HttpStatusCode.Gone); }

        [Route("throws")]
        public string GetThrows() { throw new InvalidOperationException("Boom"); }

        [Route("item/{id:int}")]
        public string GetItem(int id) { return "item " + id; }

        [Route("text")]
        public HttpResponseMessage GetText() { return new HttpResponseMessage { Content = new StringContent("plain text") }; }

        [Route("echo")]
        public IHttpActionResult PostEcho([FromBody] Models.Product product) { return Ok(product); }

        [Route("query")]
        public IHttpActionResult GetQuery(string name, int count = 1) { return Ok(new { name, count }); }
    }
}
