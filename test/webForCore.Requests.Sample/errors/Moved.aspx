<%@ Page Language="C#" %>
<%
    // An httpErrors ExecuteURL page that redirects permanently, as Subtext's BlogRequestModule does for its error page's
    // URL: the Location carries the child request's Path and RawUrl.
    Response.RedirectPermanent("/errors/NotFound.aspx?path=" + HttpUtility.UrlEncode(Request.Path) + "&rawurl=" + HttpUtility.UrlEncode(Request.RawUrl), true);
%>
