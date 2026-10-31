<%@ Page Language="C#" %>
<%@ Import Namespace="System.Linq" %>
<script runat="server">
    // Issue #8: data controls with ItemType call System.Web.DynamicData's EnableDynamicData in OnInit, as on .NET
    // Framework; System.Web.DynamicData wasn't built, so every strongly-typed control failed to load it. eShopLegacyWebForms'
    // catalog is a ListView with ItemType.
    public IQueryable<IISSample.Book> GetBooks()
    {
        return new[]
        {
            new IISSample.Book { Id = 1, Title = "First book" },
            new IISSample.Book { Id = 2, Title = "Second book" },
        }.AsQueryable();
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Words.DataSource = new[] { "alpha", "beta" };
        Words.DataBind();
    }
</script>
<html><body><form runat="server">
<asp:Repeater ID="Words" runat="server" ItemType="System.String">
    <ItemTemplate>[repeater <%# Item %>]</ItemTemplate>
</asp:Repeater>
<asp:ListView ID="Books" runat="server" ItemType="IISSample.Book" SelectMethod="GetBooks">
    <ItemTemplate>[listview <%#: Item.Title %>]</ItemTemplate>
</asp:ListView>
<asp:GridView ID="Grid" runat="server" ItemType="IISSample.Book" SelectMethod="GetBooks" AutoGenerateColumns="false" DataKeyNames="Id">
    <Columns>
        <asp:TemplateField><ItemTemplate>[gridview <%#: Item.Id %> <%#: Item.Title %>]</ItemTemplate></asp:TemplateField>
    </Columns>
</asp:GridView>
</form></body></html>
