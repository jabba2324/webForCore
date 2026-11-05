using System.Linq;
using System.Security.Claims;
using System.Web.Http;

namespace OidcSample.Controllers
{
    // What the bearer token said about the caller.
    [Authorize]
    public class MeController : ApiController
    {
        public string Get()
        {
            var identity = (ClaimsIdentity)User.Identity;
            string[] shown = { "sub", "scope", "scp", "client_id", "aud" };
            return "name=" + identity.Name + "; authenticationType=" + identity.AuthenticationType + "; " +
                string.Join("; ", identity.Claims.Where(c => shown.Contains(c.Type)).Select(c => c.Type + "=" + c.Value).OrderBy(c => c, System.StringComparer.Ordinal));
        }
    }
}
