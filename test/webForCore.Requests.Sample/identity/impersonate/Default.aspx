<%@ Page Language="C#" %>
<%-- #165: a page under <identity impersonate="true"/> runs; on Linux there is no token to impersonate. --%>
<%
    Response.ContentType = "text/plain";
    Response.Write("LOGON_USER: [" + Request.ServerVariables["LOGON_USER"] + "]\n");
    Response.Write("AUTH_TYPE: [" + Request.ServerVariables["AUTH_TYPE"] + "]\n");
    Response.Write("ran\n");
%>
