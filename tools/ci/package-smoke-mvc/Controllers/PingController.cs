using System.Web.Http;
using Microsoft.AspNet.SignalR;

namespace SmokeMvc.Controllers
{
    // #195: Web API finds its controllers, and SignalR its hubs, by scanning every assembly of the application
    // (DefaultHttpControllerTypeResolver, ReflectedHubDescriptorProvider). With Microsoft's System.Data.SqlClient, on which
    // Assembly.GetTypes() throws (#41), they must use the types that load, as they do on .NET Framework, and find these.
    public class PingController : ApiController
    {
        public string Get()
        {
            return "pong";
        }
    }

    public class SmokeHub : Hub
    {
        public string Echo(string text)
        {
            return text;
        }
    }
}
