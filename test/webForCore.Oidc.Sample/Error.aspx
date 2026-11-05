<%@ Page Language="C#" %>
<%-- Where the template's AuthenticationFailed handler sends the browser. --%>
<!DOCTYPE html>
<html><head><title>Sign-in error</title></head><body><p>Sign-in failed: <%= HttpUtility.HtmlEncode(Request.QueryString["type"]) %></p></body></html>
