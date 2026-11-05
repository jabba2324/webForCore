<%@ Page Language="C#" EnableSessionState="True" %>
<%@ Import Namespace="SessionCacheSample" %>
<script runat="server">
    // op=set&key=&value=, setbasket&key=&owner=&items=, setnotserializable&key=, abandon, timeout&minutes=, hold&ms=
    // (keeps the session's exclusive lock that long). Writes the instance, the session and when the page ran.
    protected void Page_Load(object sender, EventArgs e)
    {
        long entered = DateTime.UtcNow.Ticks;
        Response.ContentType = "text/plain";
        string key = Request.QueryString["key"];
        switch (Request.QueryString["op"])
        {
            case "set": Session[key] = Request.QueryString["value"]; break;
            case "setbasket": Session[key] = new Basket { Owner = Request.QueryString["owner"], Items = int.Parse(Request.QueryString["items"]) }; break;
            case "addtobasket": ((Basket)Session[key]).Items++; break;
            case "setnotserializable": Session[key] = new NotSerializableValue { Text = "x" }; break;
            case "abandon": Session.Abandon(); break;
            case "timeout": Session.Timeout = int.Parse(Request.QueryString["minutes"]); break;
            case "hold": System.Threading.Thread.Sleep(int.Parse(Request.QueryString["ms"])); Session["held"] = DateTime.UtcNow.Ticks.ToString(); break;
        }
        Response.Write(SessionReport.Write(Session, entered));
    }
</script>
