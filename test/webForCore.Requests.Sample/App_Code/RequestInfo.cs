using System;
using System.IO;
using System.Web;

// The request's paths as the application sees them, as text (Echo.aspx and the default documents).
public static class RequestInfo
{
    public static void Write(HttpContext context)
    {
        HttpRequest request = context.Request;
        HttpResponse response = context.Response;
        response.ContentType = "text/plain";
        Line(response, "Path", request.Path);
        Line(response, "FilePath", request.FilePath);
        Line(response, "PathInfo", request.PathInfo);
        Line(response, "RawUrl", request.RawUrl);
        Line(response, "Url", request.Url.ToString());
        Line(response, "Url.AbsolutePath", request.Url.AbsolutePath);
        Line(response, "CurrentExecutionFilePath", request.CurrentExecutionFilePath);
        Line(response, "AppRelativeCurrentExecutionFilePath", request.AppRelativeCurrentExecutionFilePath);
        Line(response, "QueryString", request.QueryString.ToString());
        Line(response, "PhysicalPath exists", File.Exists(request.PhysicalPath).ToString());
        // IIS's HTTP_URL, UNENCODED_URL and CACHE_URL are the integrated pipeline's only: ServerVariables.aspx.
        foreach (string name in new[] { "URL", "PATH_INFO", "SCRIPT_NAME" })
            Line(response, name, request.ServerVariables[name]);
        System.Web.Routing.RouteData route = request.RequestContext.RouteData;
        if (route != null && route.Route != null)
            foreach (var value in route.Values)
                Line(response, "Route " + value.Key, Convert.ToString(value.Value));
    }

    static void Line(HttpResponse response, string name, string value)
    {
        response.Write(name + ": " + (value == null ? "(null)" : "[" + value + "]") + "\n");
    }
}
