<%@ Page Language="C#" %>
target=<%= Request.Path %>;method=<%= Request.HttpMethod %>;to=<%= Request.QueryString["to"] %>;from=<%= Request.QueryString["from"] %>;field=<%= Request.Form["field"] %>;transferred=<%= Request.Headers["X-Transferred"] %>;
