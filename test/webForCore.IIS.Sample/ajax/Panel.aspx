<%@ Page Language="C#" %>
<%@ Import Namespace="System.Web.Services" %>
<script runat="server">
    // ASP.NET AJAX (System.Web.Extensions): an UpdatePanel refreshed by a partial postback, and a page method that the
    // ScriptManager's PageMethods proxy calls with JSON.
    protected void Add_Click(object sender, EventArgs e)
    {
        int total = int.Parse(Total.Text) + int.Parse(Amount.Text);
        Total.Text = total.ToString();
        Mode.Text = ScriptManager1.IsInAsyncPostBack ? "async" : "full";
    }

    [WebMethod]
    public static string Greet(string name)
    {
        return "Hello, " + name;
    }
</script>
<!DOCTYPE html>
<html>
<head runat="server"><title>AJAX</title></head>
<body>
    <form id="form1" runat="server">
        <asp:ScriptManager ID="ScriptManager1" runat="server" EnablePageMethods="true" />
        <asp:Label ID="Outside" runat="server" Text="outside" />
        <asp:UpdatePanel ID="Panel1" runat="server">
            <ContentTemplate>
                <asp:TextBox ID="Amount" runat="server" Text="5" />
                <asp:Button ID="Add" runat="server" Text="Add" OnClick="Add_Click" />
                <span id="result">total=<asp:Label ID="Total" runat="server" Text="10" />;mode=<asp:Label ID="Mode" runat="server" /></span>
            </ContentTemplate>
        </asp:UpdatePanel>
    </form>
</body>
</html>
