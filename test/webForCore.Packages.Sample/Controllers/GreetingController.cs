using System.Web.Http;
using PackagesSample.Services;

namespace PackagesSample.Controllers
{
    // Constructed by Ninject (UseNinjectWebApi's dependency resolver), with a transient dependency that has a
    // request-scoped one, and a request-scoped one of its own.
    public class GreetingController : ApiController
    {
        private readonly IGreeter _greeter;
        private readonly IRequestService _request;

        public GreetingController(IGreeter greeter, IRequestService request)
        {
            _greeter = greeter;
            _request = request;
        }

        public IHttpActionResult Get(string name = "world") => Ok(new
        {
            message = _greeter.Greet(name),
            resolver = Configuration.DependencyResolver.GetType().FullName,
            // Ninject's own behaviour, on IIS too: Ninject.Web.WebApi.OwinHost's request scope is a named scope
            // (Ninject.Extensions.NamedScope) that reaches a controller's dependencies only with
            // Ninject.Extensions.ContextPreservation, which none of these packages depends on. Without it InRequestScope
            // is transient in Web API controllers.
            requestSame = ReferenceEquals(_request, _greeter.Request),
        });
    }
}
