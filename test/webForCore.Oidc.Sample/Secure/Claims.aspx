<%@ Page Language="C#" %>
<%@ Import Namespace="System.Linq" %>
<%@ Import Namespace="System.Security.Claims" %>
<%-- HttpContext.User, as Katana's System.Web host sets it from the sign-in cookie. --%>
<!DOCTYPE html>
<html><head><title>Claims</title></head><body><pre>
name=<%= HttpUtility.HtmlEncode(User.Identity.Name) %>
authenticated=<%= User.Identity.IsAuthenticated %>
authenticationType=<%= User.Identity.AuthenticationType %>
inRole(Writer)=<%= User.IsInRole("Writer") %>
<% foreach (var claim in ((ClaimsIdentity)User.Identity).Claims.OrderBy(c => c.Type, StringComparer.Ordinal).ThenBy(c => c.Value, StringComparer.Ordinal)) { %>claim <%= HttpUtility.HtmlEncode(claim.Type) %>=<%= HttpUtility.HtmlEncode(claim.Value) %>
<% } %></pre></body></html>
