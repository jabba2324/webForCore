<%@ Page Language="C#" %>
<%@ Import Namespace="System.Data.Linq" %>
<script runat="server">
    // LINQ to SQL's Binary, compiled at run time against the System.Data.Linq that Web.config lists (issue #142).
    protected void Page_Load(object sender, EventArgs e)
    {
        var binary = new Binary(new byte[] { 1, 2, 3 });
        Output.Text = "binary " + binary.Length + " bytes, System.Data.Linq " + typeof(Binary).Assembly.GetName().Version;
    }
</script>
<asp:Literal ID="Output" runat="server" />
