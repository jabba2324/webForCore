<%@ Application Language="C#" %>
<script runat="server">
    void Application_Start(object sender, EventArgs e)
    {
        System.Threading.Interlocked.Increment(ref DomainState.ApplicationStarts);
    }
</script>
