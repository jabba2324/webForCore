<%@ Page Language="C#" %>
https=<%= Request.ServerVariables["HTTPS"] %>;port_secure=<%= Request.ServerVariables["SERVER_PORT_SECURE"] %>;secure=<%= Request.IsSecureConnection %>;scheme=<%= Request.Url.Scheme %>;
instance=<%= Request.ServerVariables["INSTANCE_ID"] %>;cert_flags=<%= Request.ServerVariables["CERT_FLAGS"] %>;present=<%= Request.ClientCertificate.IsPresent %>;
cert_subject=<%= Request.ServerVariables["CERT_SUBJECT"] %>;subject=<%= Request.ClientCertificate.Subject %>;serial=<%= Request.ClientCertificate.SerialNumber %>;
bytes=<%= Request.ClientCertificate.Certificate.Length %>;connected=<%= Response.IsClientConnected %>;
remote=<%= Request.ServerVariables["REMOTE_ADDR"] %>;local=<%= Request.ServerVariables["LOCAL_ADDR"] %>;port=<%= Request.ServerVariables["SERVER_PORT"] %>;host=<%= Request.ServerVariables["HTTP_HOST"] %>;raw=<%= Request.RawUrl %>;keysize=<%= Request.ServerVariables["HTTPS_KEYSIZE"] %>;
