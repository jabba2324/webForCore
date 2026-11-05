<%@ Page Language="C#" %>
<%@ OutputCache Duration="300" VaryByParam="*" %>
<script runat="server">
    // #184: output cache substitution (Response.WriteSubstitution, asp:Substitution): each hit runs the callback and
    // its bytes become a buffer element of their own (HttpSubstBlockResponseElement.Substitute).
    static string Now(HttpContext context) { return "subst:" + context.Request.QueryString["c"] + ":" + Guid.NewGuid().ToString("N"); }

    protected override void Render(HtmlTextWriter writer)
    {
        Response.ContentType = "text/plain";
        Response.Write("cached:" + Guid.NewGuid().ToString("N") + "|");
        Response.WriteSubstitution(Now);
        Response.Write("|" + new string(Request.QueryString["c"][0], int.Parse(Request.QueryString["n"])));
    }
</script>
