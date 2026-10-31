<%@ Page Language="C#" %>
<script runat="server">
    protected void Page_Load(object sender, EventArgs e)
    {
        Session["visits"] = 1 + (int?)Session["visits"] ?? 1;
        Message.Text = "webForCore package smoke test: " + Environment.Version.Major + "/" + HttpRuntime.TargetFramework;
    }
</script>
<!DOCTYPE html>
<html>
<body>
    <form id="form1" runat="server">
        <asp:Label ID="Message" runat="server" />
    </form>
</body>
</html>
