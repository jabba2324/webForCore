<%@ Page Language="C#" %>
<% Response.Write("before CompleteRequest\n"); Context.ApplicationInstance.CompleteRequest(); Response.Write("after CompleteRequest\n"); %>
