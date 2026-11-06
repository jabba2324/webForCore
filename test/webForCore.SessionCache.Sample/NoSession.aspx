<%@ Page Language="C#" EnableSessionState="False" %>
<script runat="server">
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write(SessionCacheSample.SessionProbe.Report(Context));
    }
</script>
