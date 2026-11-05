<%@ WebHandler Language="C#" Class="UnbufferedWriteHandler" %>
using System;
using System.IO;
using System.Text;
using System.Web;

// #184: Response.Write(string) with BufferOutput = false (Bonobo Git Server's smart HTTP: GitCmdResult writes the refs
// advertisement header with Response.Write, turns buffering off and copies git's output to Response.OutputStream).
//   ?mode=write              Write("hello"), Write(" world"), the issue's repro.
//   ?mode=git                Bonobo's order: Write(header), BufferOutput = false, OutputStream copies, Write again.
//   ?mode=fill&c=x&n=1024    n characters 'c', written twice (2n in all), in ?encoding= (utf-8, utf-16, utf-32).
public class UnbufferedWriteHandler : IHttpHandler
{
    public bool IsReusable { get { return false; } }

    public void ProcessRequest(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = "text/plain";
        switch (context.Request.QueryString["mode"])
        {
            case "write":
                response.BufferOutput = false;
                response.Write("hello");
                response.Write(" world");
                break;
            case "git":
                response.Write("001e# service=git-upload-pack\n");
                response.Write("0000");
                response.BufferOutput = false;
                var pack = new byte[100000];
                for (int i = 0; i < pack.Length; i++) pack[i] = (byte)('a' + i % 26);
                new MemoryStream(pack).CopyTo(response.OutputStream, 4096);
                response.OutputStream.Write(pack, 0, 10);
                response.Write("|end");
                break;
            case "fill":
                string encoding = context.Request.QueryString["encoding"];
                if (!string.IsNullOrEmpty(encoding)) response.ContentEncoding = Encoding.GetEncoding(encoding);
                response.BufferOutput = false;
                var text = new string(context.Request.QueryString["c"][0], int.Parse(context.Request.QueryString["n"]));
                response.Write(text);
                response.Write(text);
                break;
        }
    }
}
