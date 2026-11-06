<%@ Page Language="C#" %>
<script runat="server">
    // No EnableSessionState: <pages enableSessionState> decides (true by default; PagesOff/ has it false).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write(SessionCacheSample.SessionProbe.Report(Context));
    }
</script>
