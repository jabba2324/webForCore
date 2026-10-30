<%@ Page Language="C#" %>
<%
    // Runs past <httpRuntime executionTimeout="2">: the client gets ASP.NET's "Request timed out." while this keeps going,
    // until Request.TimedOutToken is canceled.
    var start = DateTime.UtcNow;
    while (!Request.TimedOutToken.IsCancellationRequested && DateTime.UtcNow - start < TimeSpan.FromSeconds(60))
        System.Threading.Thread.Sleep(200);
    Response.Write("finished after " + (DateTime.UtcNow - start).TotalSeconds);
%>
