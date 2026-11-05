<%@ Page Language="C#" EnableSessionState="ReadOnly" %>
<%@ Import Namespace="SessionCacheSample" %>
<script runat="server">
    // op=hold&ms= runs that long with the session read-only (no exclusive lock).
    protected void Page_Load(object sender, EventArgs e)
    {
        long entered = DateTime.UtcNow.Ticks;
        Response.ContentType = "text/plain";
        if (Request.QueryString["op"] == "hold") System.Threading.Thread.Sleep(int.Parse(Request.QueryString["ms"]));
        Response.Write(SessionReport.Write(Session, entered));
    }
</script>
