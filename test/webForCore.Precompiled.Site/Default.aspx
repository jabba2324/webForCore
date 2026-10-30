<%@ Page Language="C#" MasterPageFile="~/Site.master" CodeFile="Default.aspx.cs" Inherits="DefaultPage" %>
<%@ Register Src="~/Controls/Badge.ascx" TagPrefix="gate" TagName="Badge" %>
<asp:Content ContentPlaceHolderID="Main" runat="server">
greeting=<asp:Label ID="Greeting" runat="server" />;
started=<%= ASP.global_asax.StartedBy %>;
<gate:Badge ID="Badge" runat="server" Text="ok" />
compiled=<%= GetType().Assembly.GetName().Name.StartsWith("App_Web_") %>;
</asp:Content>
