<%@ Page Language="C#" %>
<% Response.Write("before Redirect\n"); Response.Redirect("~/Echo.aspx", true); Response.Write("after Redirect\n"); %>
