<%@ Page Language="C#" %>
<script runat="server">
    // Response.Redirect with an application-relative URL (#190).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.Redirect("~/Probe.aspx?from=redirect", false);
        Context.ApplicationInstance.CompleteRequest();
    }
</script>
