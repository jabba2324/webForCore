<%@ Page Language="C#" ContentType="text/plain" %>
<%@ Import Namespace="System.Web.Security" %>
<script runat="server">
    // Signs in and starts a session; writes the cookies' attributes (#190: their paths; #194: Secure with requireSSL).
    protected override void Render(HtmlTextWriter writer)
    {
        Session["started"] = "yes";
        if (Request.QueryString["user"] != null)
        {
            try
            {
                FormsAuthentication.SetAuthCookie(Request.QueryString["user"], false);
            }
            catch (HttpException error)
            {
                writer.Write("error=" + error.Message + "\n");
            }
        }
        writer.Write("return_url=" + Request.QueryString["ReturnUrl"] + "\n");
        foreach (string name in Response.Cookies.AllKeys)
        {
            var cookie = Response.Cookies[name];
            writer.Write("cookie=" + name + ";path=" + cookie.Path + ";secure=" + cookie.Secure + ";httponly=" + cookie.HttpOnly + "\n");
        }
    }
</script>
