<%@ Page Language="C#" %>
<%
    try { Response.Write("value=" + System.Web.Configuration.WebConfigurationManager.AppSettings["X"]); }
    catch (System.Configuration.ConfigurationErrorsException e) { Response.Write("error=" + e.Message); }
%>
