<%@ Page Language="C#" %>
<script runat="server">
    // Issue #212: __VIEWSTATEGENERATOR is a hash of the page's directory and class name (Page.GetClientStateIdentifier),
    // which on .NET Framework is StringComparer.InvariantCultureIgnoreCase's; webForCore rendered another value for the
    // same page (PR #206's IIS comparison of the LinqDataSource pages), so a page posted between IIS and webForCore
    // servers during a cutover, or a tool comparing it, saw a different one.
    protected void Page_Load(object sender, EventArgs e)
    {
        ViewState["posted"] = IsPostBack;
    }
</script>
<html>
<body>
    <form id="form1" runat="server">
        <asp:Label ID="Result" runat="server" Text="view state generator" />
    </form>
</body>
</html>
