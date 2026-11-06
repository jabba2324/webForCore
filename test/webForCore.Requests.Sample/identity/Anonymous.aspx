<%@ Page Language="C#" %>
<%-- #165: what an anonymous request has of Windows identity. IIS: LOGON_USER and AUTH_TYPE are empty and LogonUserIdentity
     is the anonymous account's (IUSR, or the application pool's identity). --%>
<%
    Response.ContentType = "text/plain";
    Response.Write("LOGON_USER: [" + Request.ServerVariables["LOGON_USER"] + "]\n");
    Response.Write("AUTH_TYPE: [" + Request.ServerVariables["AUTH_TYPE"] + "]\n");
    Response.Write("User: [" + User.Identity.Name + "] IsAuthenticated " + User.Identity.IsAuthenticated + "\n");
    System.Security.Principal.WindowsIdentity identity = Request.LogonUserIdentity;
    Response.Write("LogonUserIdentity: " + (identity == null ? "(null)" : "[" + identity.Name + "] AuthenticationType [" + identity.AuthenticationType + "] IsAuthenticated " + identity.IsAuthenticated) + "\n");
%>
