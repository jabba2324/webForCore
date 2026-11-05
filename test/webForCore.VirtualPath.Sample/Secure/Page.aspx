<%@ Page Language="C#" ContentType="text/plain" %>
<script runat="server">
    // Only for signed-in users (Web.config): anonymous requests go to FormsAuthentication.LoginUrl.
    protected override void Render(HtmlTextWriter writer)
    {
        writer.Write("user=" + User.Identity.Name + "\n");
    }
</script>
