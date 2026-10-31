<%@ Page Language="C#" %>
rewrite default page, slug=<%= Server.HtmlEncode(Request.QueryString["slug"]) %>
