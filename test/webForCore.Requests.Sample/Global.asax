<%@ Application Language="C#" %>
<script runat="server">
    void Application_Start(object sender, EventArgs e)
    {
        // #126: as Wingtip Toys maps its product pages (GetRouteUrl("ProductByNameRoute", new { productName = ... })).
        System.Web.Routing.RouteTable.Routes.MapPageRoute("ProductByNameRoute", "Product/{productName}", "~/Echo.aspx");
    }
</script>
