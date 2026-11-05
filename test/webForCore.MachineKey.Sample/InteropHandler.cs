using System.Collections.Generic;
using System.IO;
using System.Web;
using System.Web.Configuration;
using System.Web.Script.Serialization;
using MachineKeyInterop;

namespace MachineKeySample
{
    // interop.axd?op=produce: this application's outputs for its <machineKey> (Interop.Produce), as JSON; the cookies are
    // issued and read by the cookie middleware (Startup), not here.
    // interop.axd?op=consume (POST, JSON entries): reads another runtime's outputs (Interop.Consume) and answers the results.
    public sealed class InteropHandler : IHttpHandler
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            string config = WebConfigurationManager.AppSettings["interop:config"];
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            context.Response.ContentType = "application/json";
            switch (context.Request.QueryString["op"])
            {
                case "produce":
                    context.Response.Write(json.Serialize(Interop.Produce(config, owin: false)));
                    break;
                case "consume":
                    string body;
                    using (var reader = new StreamReader(context.Request.InputStream)) body = reader.ReadToEnd();
                    context.Response.Write(json.Serialize(Interop.Consume(json.Deserialize<List<InteropEntry>>(body), owin: false)));
                    break;
                default:
                    context.Response.StatusCode = 400;
                    break;
            }
        }
    }
}
