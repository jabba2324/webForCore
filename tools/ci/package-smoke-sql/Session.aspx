<%@ Page Language="C#" %>
<%-- The SQL Server session state provider: a counter kept between requests. --%>
<%
    Session["count"] = (Session["count"] == null ? 0 : (int)Session["count"]) + 1;
    Response.ContentType = "text/plain";
    Response.Write("session: " + Session["count"] + "\n");
%>
