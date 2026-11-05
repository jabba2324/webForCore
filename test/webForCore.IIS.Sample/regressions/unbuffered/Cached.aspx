<%@ Page Language="C#" %>
<%@ OutputCache Duration="300" VaryByParam="*" %>
<script runat="server">
    // #184: output caching keeps the response's buffers (HttpWriter.GetSnapshot): a buffer nearly full is cached as it
    // is (DisableRecycling), one with more free space as a copy (Clone). Every hit must answer the first response's
    // bytes, while other requests rent and write buffers. ?n= characters of ?c=, then the time the page ran.
    protected override void Render(HtmlTextWriter writer)
    {
        Response.ContentType = "text/plain";
        Response.Write(new string(Request.QueryString["c"][0], int.Parse(Request.QueryString["n"])));
        Response.Write("|" + Guid.NewGuid().ToString("N"));
    }
</script>
