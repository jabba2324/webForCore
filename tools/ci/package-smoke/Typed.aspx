<%@ Page Language="C#" %>
<script runat="server">
    // A data control with ItemType loads System.Web.DynamicData (EnableDynamicData), which the webForCore package must
    // bring (issue #8).
    protected void Page_Load(object sender, EventArgs e)
    {
        Words.DataSource = new[] { "typed", "repeater" };
        Words.DataBind();
    }
</script>
<asp:Repeater ID="Words" runat="server" ItemType="System.String"><ItemTemplate>[<%# Item %>]</ItemTemplate></asp:Repeater>
