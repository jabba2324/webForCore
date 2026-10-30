using Microsoft.AspNet.SignalR;
using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(IISSample.SignalRStartup))]

namespace IISSample
{
    // SignalR 2 on System.Web, the way an ASP.NET 4.x app wires it: Katana's SystemWeb host runs this at application
    // start (OwinStartup), MapSignalR serves /signalr.
    public class SignalRStartup
    {
        public void Configuration(IAppBuilder app)
        {
            app.MapSignalR();
        }
    }

    public class GateHub : Hub
    {
        public string Echo(string message)
        {
            // A call back to the caller, then the return value.
            Clients.Caller.echoed("pushed: " + message);
            return "hub: " + message + " (" + Context.Request.Url.AbsolutePath + ")";
        }
    }
}
