<%@ Page Language="C#" EnableSessionState="False" %>
<script runat="server">
    // A forms ticket, encrypted with the <machineKey> of configs/SiteId.config, whose keys are IsolateByAppId: op=encrypt&name=
    // writes one for that name, op=decrypt&ticket= the name in one (or the error). The keys depend on HttpRuntime.AppDomainAppId,
    // which is /LM/W3SVC/<WebForCore:Host:SiteId>/ROOT (#210).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("instance=" + Environment.GetEnvironmentVariable("SESSIONCACHE_INSTANCE") + "\n");
        Response.Write("appid=" + HttpRuntime.AppDomainAppId + "\n");
        Response.Write("applmdpath=" + Request.ServerVariables["APPL_MD_PATH"] + "\n");
        Response.Write("instanceid=" + Request.ServerVariables["INSTANCE_ID"] + "\n");
        Response.Write("instancemetapath=" + Request.ServerVariables["INSTANCE_META_PATH"] + "\n");
        switch (Request.QueryString["op"])
        {
            case "encrypt":
                var ticket = new FormsAuthenticationTicket(2, Request.QueryString["name"], DateTime.Now, DateTime.Now.AddHours(1), false, "");
                Response.Write("ticket=" + FormsAuthentication.Encrypt(ticket) + "\n");
                break;
            case "decrypt":
                try
                {
                    var decrypted = FormsAuthentication.Decrypt(Request.QueryString["ticket"]);
                    Response.Write("name=" + (decrypted == null ? "" : decrypted.Name) + "\n");
                }
                catch (Exception error)
                {
                    Response.Write("error=" + error.GetType().FullName + "\n");
                }
                break;
        }
    }
</script>
