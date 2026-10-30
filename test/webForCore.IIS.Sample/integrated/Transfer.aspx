<%@ Page Language="C#" %>
<%
    var headers = new System.Collections.Specialized.NameValueCollection { { "X-Transferred", "yes" } };
    if (Request.QueryString["form"] == "1") Server.TransferRequest("Target.aspx", true);
    else Server.TransferRequest("Target.aspx?to=target", false, "GET", headers);
    Response.Write("not transferred");
%>
