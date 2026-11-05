<%@ Page Language="C#" %>
<%
    // The URL server variables that only IIS's integrated pipeline has; webForCore's pipeline is the classic one.
    Response.ContentType = "text/plain";
    foreach (string name in new[] { "HTTP_URL", "UNENCODED_URL", "CACHE_URL" })
        Response.Write(name + ": " + (Request.ServerVariables[name] ?? "(null)") + "\n");
%>
