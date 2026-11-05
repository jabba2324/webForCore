<%@ Page Language="C#" %>
<% Response.Write("before Transfer\n"); Server.Transfer("~/Echo.aspx"); Response.Write("after Transfer\n"); %>
