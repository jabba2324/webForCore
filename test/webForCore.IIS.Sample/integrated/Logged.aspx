<%@ Page Language="C#" %>
log=<%= IntegratedModule.Logged.TryGetValue("log:/integrated/Default.aspx", out var l) ? l : 0 %>;postlog=<%= IntegratedModule.Logged.TryGetValue("postlog:/integrated/Default.aspx", out var p) ? p : 0 %>;
