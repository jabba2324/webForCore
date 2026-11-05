using System;
using System.IO;
using System.Web;

// #192: <customErrors>, <httpErrors> and Response.TrySkipIisCustomErrors (the pages under /errors/), and
// <httpRuntime maxRequestLength> and requestLengthDiskThreshold against request filtering's maxAllowedContentLength (the
// handlers under /limits/). Each folder's Web.config sets them; the pages are the same in every folder. Written for the
// C# compiler of .NET Framework 4.8, which IIS compiles App_Code with.
public static class ErrorPages
{
    public static void Throw()
    {
        throw new InvalidOperationException("Thrown by the page.");
    }

    public static void Http404()
    {
        throw new HttpException(404, "Not found by the page.");
    }

    // An error status the page sets itself, with a body: customErrors doesn't apply, <httpErrors> may.
    public static void Status500(HttpContext context, bool trySkip)
    {
        HttpResponse response = context.Response;
        if (trySkip) response.TrySkipIisCustomErrors = true;
        response.StatusCode = 500;
        response.ContentType = "text/plain";
        response.Write("500 set by the page" + (trySkip ? ", TrySkipIisCustomErrors" : "") + "\n");
    }

    // The error pages customErrors sends to (defaultRedirect and <error statusCode>).
    public static void Describe(HttpContext context, string page)
    {
        HttpResponse response = context.Response;
        Exception error = context.Server.GetLastError();
        response.ContentType = "text/plain";
        response.Write(page + "\n");
        response.Write("status: " + response.StatusCode + "\n");
        response.Write("aspxerrorpath: " + (context.Request.QueryString["aspxerrorpath"] ?? "(none)") + "\n");
        response.Write("Path: " + context.Request.Path + "\n");
        response.Write("RawUrl: " + context.Request.RawUrl + "\n");
        response.Write("last error: " + (error == null ? "(none)" : error.GetType().FullName + ": " + error.Message) + "\n");
        if (error != null && error.InnerException != null)
            response.Write("inner error: " + error.InnerException.GetType().FullName + ": " + error.InnerException.Message + "\n");
    }
}

// Request.Form, Request.Files and Request.InputStream read the whole body (HttpRequest.GetEntireRawContent), which
// enforces maxRequestLength and buffers a body over requestLengthDiskThreshold to a file in the compilation folder's
// "uploads" folder. Each handler says what it read and whether a file was written for it there.
public abstract class LimitsHandler : IHttpHandler
{
    public bool IsReusable
    {
        get { return false; }
    }

    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "text/plain";
        int before = Uploads();
        string read = Read(context.Request);
        context.Response.Write(read + "\n");
        context.Response.Write("Content-Length: " + context.Request.ContentLength + "\n");
        context.Response.Write("buffered to disk: " + (Uploads() > before) + "\n");
    }

    protected abstract string Read(HttpRequest request);

    static int Uploads()
    {
        string uploads = Path.Combine(HttpRuntime.CodegenDir, "uploads");
        return Directory.Exists(uploads) ? Directory.GetFiles(uploads).Length : 0;
    }
}

public class LimitsFormHandler : LimitsHandler
{
    protected override string Read(HttpRequest request)
    {
        string text = "form: " + request.Form.Count + " fields";
        foreach (string key in request.Form.AllKeys)
            text += ", " + key + " " + request.Form[key].Length + " chars";
        return text;
    }
}

public class LimitsFilesHandler : LimitsHandler
{
    protected override string Read(HttpRequest request)
    {
        string text = "files: " + request.Files.Count;
        for (int i = 0; i < request.Files.Count; i++)
        {
            HttpPostedFile file = request.Files[i];
            text += ", " + request.Files.AllKeys[i] + " " + file.FileName + " " + file.ContentLength + " bytes";
        }
        return text + "; form: " + request.Form.Count + " fields";
    }
}

public class LimitsInputHandler : LimitsHandler
{
    protected override string Read(HttpRequest request)
    {
        Stream input = request.InputStream;
        long length = 0;
        byte[] buffer = new byte[65536];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) length += read;
        return "input: " + length + " bytes";
    }
}

// Doesn't read the body: maxRequestLength isn't checked.
public class LimitsIgnoreHandler : LimitsHandler
{
    protected override string Read(HttpRequest request)
    {
        return "body not read";
    }
}

// Catches the HttpException that Request.Form throws over maxRequestLength.
public class LimitsCatchHandler : LimitsHandler
{
    protected override string Read(HttpRequest request)
    {
        try
        {
            return "form: " + request.Form.Count + " fields";
        }
        catch (HttpException e)
        {
            return "HttpException " + e.GetHttpCode() + ": " + e.Message;
        }
    }
}
