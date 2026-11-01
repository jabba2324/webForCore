using System.Web;
using System.Web.Services;
using System.Web.Services.Description;
using System.Web.Services.Protocols;

// Compiled into the application's assembly, as Visual Studio's generic handler template and SOAP client proxies are:
// WsiProfiles and SoapBindingUse are in the packages' System.Web.Services, and in the System.Web.Services.Description
// package that CoreWCF brings, whose compile reference the build targets must drop (issue #34: CS0433; mojoPortal's
// .ashx handlers, Subtext's Web Reference proxy).
[WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
public class PingHandler : IHttpHandler
{
    public static readonly SoapBindingUse Use = SoapBindingUse.Literal;

    public bool IsReusable => true;

    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "text/plain";
        context.Response.Write("ping " + Use);
    }
}
