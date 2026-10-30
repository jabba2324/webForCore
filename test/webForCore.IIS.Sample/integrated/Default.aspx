<%@ Page Language="C#" %>
notifications=<%= string.Join(",", (System.Collections.Generic.List<string>)Context.Items["notifications"]) %>;
now=<%= Context.CurrentNotification %>;
header=<%= Request.Headers["X-Integrated"] %>;servervariable=<%= Request.ServerVariables["HTTP_X_SERVER_VARIABLE"] %>;headervar=<%= Request.ServerVariables["HTTP_X_INTEGRATED"] %>;
steps=<%= IntegratedModule.Steps > 0 %>;
