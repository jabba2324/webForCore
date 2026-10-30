<%@ Page Language="C#" %>
Shop item <%= HttpUtility.HtmlEncode(Request.QueryString["id"]) %> (<%= Request.Path %>)
