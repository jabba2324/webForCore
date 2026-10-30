<%@ Page Language="C#" %>
Product <%= HttpUtility.HtmlEncode(Request.QueryString["id"]) %> (<%= Request.Path %>)
