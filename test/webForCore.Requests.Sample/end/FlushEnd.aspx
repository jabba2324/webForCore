<%@ Page Language="C#" %>
<% Response.Write("before Flush\n"); Response.Flush(); Response.Write("after Flush\n"); Response.End(); %>
