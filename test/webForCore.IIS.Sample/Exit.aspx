<%@ Page Language="C#" %>
<%-- The worker process ends shortly after answering, as a crash would (RecyclingTests: webforcore-host replaces it). --%>
<script runat="server">
    protected void Page_Load(object sender, EventArgs e)
    {
        int code = int.Parse(Request["code"] ?? "42");
        System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(300); Environment.Exit(code); });
        Response.Write("exiting " + code);
    }
</script>
