<%@ Page Language="C#" %>
<%
    // #170: the request headers IIS has no index for ("unknown" headers) reach the application whatever the path, a path
    // with '(' included; and a cookieless path, /(S(...))/, is the page's, with the session id in AspFilterSessionId.
    Response.ContentType = "text/plain";
    Response.Write("Path: [" + Request.Path + "]\n");
    Response.Write("RawUrl: [" + Request.RawUrl + "]\n");
    string raw = Request.ServerVariables["ALL_RAW"] ?? string.Empty;
    foreach (string name in new[] { "X-Custom", "Prefer", "X-Requested-With", "AspFilterSessionId" })
    {
        string value = Request.Headers[name];
        Response.Write(name + ": " + (value == null ? "(null)" : "[" + value + "]") + "\n");
        if (value != null && name != "AspFilterSessionId")
            Response.Write(name + " in ALL_RAW: " + (raw.IndexOf(name + ": " + value, StringComparison.Ordinal) >= 0) + "\n");
    }
%>
