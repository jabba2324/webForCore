<%@ Page Language="C#" ContentType="text/html" %>
<%@ Import Namespace="System.Web.Optimization" %>
<%-- The tags System.Web.Optimization renders for the bundles, with the application's virtual path (#190). --%>
<%= Scripts.Render("~/bundles/site") %>
<%= Styles.Render("~/Content/css") %>
