<%@ Page Language="C#" %>
<%@ Register TagPrefix="gate" TagName="Ansi" Src="~/regressions/encoding/AnsiControl.ascx" %>
<%@ Import Namespace="System.Web.Configuration" %>
<%-- #90: saved in windows-1252 without a BOM, as Umbraco 6's install/steps/theend.ascx is. On .NET Framework
     System.Web read such files in the ANSI code page (Encoding.Default); webForCore showed U+FFFD for each
     non-ASCII byte. --%>
<script runat="server">
    protected string FileEncoding { get { return ((GlobalizationSection)WebConfigurationManager.GetSection("system.web/globalization")).FileEncoding.WebName; } }
</script>
fileEncoding: <%= FileEncoding %>
page: You’re done, café
<gate:Ansi runat="server" />
