using System.Web;

// Registered in <system.webServer><handlers> only.
public class GateHandler : IHttpHandler
{
    public bool IsReusable => true;

    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "text/plain";
        context.Response.Write("gate handler");
    }
}
