<%@ Page Language="C#" Async="true" %>
<%@ Import Namespace="System.Net" %>
<%@ Import Namespace="System.Net.Http" %>
<script runat="server">
    // <system.net><defaultProxy> in Web.config: HttpClient and WebClient (HttpWebRequest) go through its proxy, except for
    // local addresses (bypassonlocal) and the bypass list.
    protected void Page_Load(object sender, EventArgs e)
    {
        RegisterAsyncTask(new PageAsyncTask(async () =>
        {
            IWebProxy proxy = HttpClient.DefaultProxy;
            var output = new System.Text.StringBuilder();
            output.Append("proxy=" + proxy.GetProxy(new Uri("http://gate.example.invalid/")) + ";");
            output.Append("bypassed=" + proxy.IsBypassed(new Uri("http://skip.example.invalid/")) + ";");
            output.Append("local=" + proxy.IsBypassed(new Uri("http://127.0.0.1/")) + ";");
            using (var client = new HttpClient())
                output.Append("httpclient=" + await client.GetStringAsync("http://gate.example.invalid/httpclient") + ";");
#pragma warning disable SYSLIB0014
            using (var client = new WebClient())
                output.Append("webclient=" + client.DownloadString("http://gate.example.invalid/webclient") + ";");
#pragma warning restore SYSLIB0014
            Result.Text = output.ToString();
        }));
    }
</script>
<!DOCTYPE html>
<html>
<body>
    <asp:Label ID="Result" runat="server" />
</body>
</html>
