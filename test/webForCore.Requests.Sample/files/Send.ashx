<%@ WebHandler Language="C#" Class="FilesSendHandler" %>

using System;
using System.Web;

// #187: Response.TransmitFile and WriteFile with an offset and a length, of Large.dat (140,000 bytes: more than two of
// the worker request's 64 KB chunks). ?m=transmit|writefile|writefile-all&offset=&length=. A range the file doesn't
// have is HttpResponse's exception, written with its type and parameter (the messages' formats differ between .NET
// Framework and .NET).
public class FilesSendHandler : IHttpHandler
{
    public bool IsReusable { get { return false; } }

    public void ProcessRequest(HttpContext context)
    {
        HttpResponse response = context.Response;
        response.ContentType = "text/plain";
        string path = context.Server.MapPath("~/files/Large.dat");
        long offset = long.Parse(context.Request.QueryString["offset"] ?? "0");
        long length = long.Parse(context.Request.QueryString["length"] ?? "-1");
        try
        {
            switch (context.Request.QueryString["m"])
            {
                case "transmit":
                    response.TransmitFile(path, offset, length);
                    break;
                case "writefile":
                    response.WriteFile(path, offset, length);
                    break;
                case "writefile-all":
                    response.WriteFile(path);
                    break;
                default:
                    response.StatusCode = 400;
                    break;
            }
        }
        catch (Exception e)
        {
            response.ClearContent();
            ArgumentException argument = e as ArgumentException;
            response.Write(e.GetType().FullName + (argument != null ? " (" + argument.ParamName + ")" : "") + "\n");
        }
    }
}
