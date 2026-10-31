<%@ Page Language="C#" %>
<%@ Import Namespace="System.ComponentModel" %>
<%@ Import Namespace="System.ComponentModel.DataAnnotations" %>
<script runat="server">
    protected void Page_Load(object sender, EventArgs e)
    {
        Books.DataSource = new IISSample.Shelf().GetBooks();
        Books.DataBind();
        Table.DataSource = IISSample.Shelf.Table();
        Table.DataBind();
        // As MVC and DynamicData apps register buddy-class providers (usually in Application_Start).
        TypeDescriptor.AddProviderTransparent(new AssociatedMetadataTypeTypeDescriptionProvider(typeof(IISSample.Book), typeof(IISSample.BookMetadata)), typeof(IISSample.Book));
        var title = TypeDescriptor.GetProperties(typeof(IISSample.Book))["Title"];
        var provider = new AssociatedMetadataTypeTypeDescriptionProvider(typeof(IISSample.Book));
        var display = provider.GetTypeDescriptor(typeof(IISSample.Book)).GetProperties()["Title"].Attributes[typeof(DisplayAttribute)] as DisplayAttribute;
        Meta.Text = title.PropertyType.Name + "/" + (display != null ? display.Name : "none");
    }
</script>
<!DOCTYPE html>
<html>
<body>
    <form id="form1" runat="server">
        <asp:Repeater ID="Books" runat="server"><ItemTemplate><span class="book"><%# Eval("Title") %>|<%# Eval("Size") %></span></ItemTemplate></asp:Repeater>
        <asp:GridView ID="Grid" runat="server" DataSourceID="ShelfSource" AutoGenerateColumns="true" />
        <asp:ObjectDataSource ID="ShelfSource" runat="server" TypeName="IISSample.Shelf" SelectMethod="GetBooks" />
        <asp:GridView ID="Table" runat="server" AutoGenerateColumns="true" />
        <asp:DetailsView ID="Details" runat="server" DataSourceID="ShelfSource" />
        <asp:Label ID="Meta" runat="server" />
    </form>
</body>
</html>
