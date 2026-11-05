using System;
using System.Web;

// #129: marks the responses of the pages under /end/ in EndRequest, the event in which FormsAuthenticationModule turns a
// 401 into its redirect. On IIS, EndRequest runs after Response.End(), Redirect(url, true), Server.Transfer and
// CompleteRequest() with the response still buffered, so the header can be added; once the response has been flushed
// (Response.Flush()) it can't.
public class EndRequestModule : IHttpModule
{
    public void Init(HttpApplication application)
    {
        application.EndRequest += (sender, e) =>
        {
            HttpContext context = ((HttpApplication)sender).Context;
            if (context.Request.Path.StartsWith("/end/", StringComparison.OrdinalIgnoreCase) && !context.Response.HeadersWritten)
                context.Response.AppendHeader("X-End-Request", "ran");
        };
    }

    public void Dispose()
    {
    }
}
