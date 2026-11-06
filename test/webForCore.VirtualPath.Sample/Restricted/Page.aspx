<%@ Page Language="C#" ContentType="text/plain" %>
<script runat="server">
    // Only for the client 203.0.113.7 (<ipSecurity> in Web.config, without proxy mode): through a proxy, when its
    // X-Forwarded-For is applied (#194).
    protected override void Render(HtmlTextWriter writer)
    {
        writer.Write("restricted " + Request.UserHostAddress + "\n");
    }
</script>
