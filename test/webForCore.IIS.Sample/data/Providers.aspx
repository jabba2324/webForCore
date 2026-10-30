<%@ Page Language="C#" %>
<%@ Import Namespace="System.Data.Common" %>
<%
    // <system.data><DbProviderFactories> in Web.config: an added provider, a removed built-in one, a kept built-in one.
    Response.Write("gate=" + DbProviderFactories.GetFactory("WebForCore.Gate").GetType().FullName + ";");
    Response.Write("oledb=" + DbProviderFactories.TryGetFactory("System.Data.OleDb", out _) + ";");
    Response.Write("sqlclient=" + DbProviderFactories.GetFactory("System.Data.SqlClient").GetType().FullName + ";");
%>
