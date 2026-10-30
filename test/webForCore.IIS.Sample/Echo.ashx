<%@ WebHandler Language="C#" Class="EchoHandler" %>
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.WebSockets;

// A WebSocket echo, the IIS 8 way: AcceptWebSocketRequest, then the function runs with an AspNetWebSocketContext.
public class EchoHandler : IHttpHandler
{
    public bool IsReusable { get { return false; } }

    public void ProcessRequest(HttpContext context)
    {
        if (!context.IsWebSocketRequest)
        {
            context.Response.Write("not a websocket request");
            return;
        }
        string protocol = context.WebSocketRequestedProtocols.Count > 0 ? context.WebSocketRequestedProtocols[0] : null;
        context.AcceptWebSocketRequest(Echo, new AspNetWebSocketOptions { SubProtocol = protocol });
    }

    static async Task Echo(AspNetWebSocketContext context)
    {
        var socket = context.WebSocket;
        var buffer = new byte[4096];
        string prefix = "echo(" + context.QueryString["tag"] + "," + context.Path + "): ";
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                break;
            }
            var reply = Encoding.UTF8.GetBytes(prefix + Encoding.UTF8.GetString(buffer, 0, result.Count));
            await socket.SendAsync(new ArraySegment<byte>(reply), WebSocketMessageType.Text, true, CancellationToken.None);
        }
    }
}
