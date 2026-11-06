<%@ Page Language="C#" EnableSessionState="False" %>
<%@ OutputCache Duration="4" VaryByParam="none" %>
<script runat="server">
    // Cached for 4 seconds, whatever the query string, in the output cache provider web.config names.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("instance=" + Environment.GetEnvironmentVariable("SESSIONCACHE_INSTANCE") + "\n");
        Response.Write("id=" + Request.QueryString["id"] + "\n");
        Response.Write("rendered=" + Guid.NewGuid().ToString("N") + "\n");
        Response.Write("provider=" + System.Web.Caching.OutputCache.DefaultProviderName + "\n");
    }
</script>
