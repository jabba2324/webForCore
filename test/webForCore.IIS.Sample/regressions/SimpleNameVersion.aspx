<%@ Page Language="C#" %>
<%@ Import Namespace="System.Reflection" %>
<script runat="server">
    // Issue #171 (JabbR): fusion doesn't check the version of an assembly without a strong name. JabbR deploys Nancy
    // 0.20.0 and Nancy.SimpleAuthentication, built against Nancy 0.21.1, and IIS runs it; webForCore threw
    // FileNotFoundException for "Nancy, Version=0.21.1.0". The sample deploys SimpleLib 1.0.0.0 and SimpleUser, built
    // against SimpleLib 2.0.0.0; neither has a strong name.
    static string Describe(Func<object> load)
    {
        try
        {
            var result = load();
            return result == null ? "null" : result.ToString();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return ex.InnerException.GetType().FullName;
        }
        catch (Exception ex)
        {
            return ex.GetType().FullName;
        }
    }

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Response.Write("SimpleUser calls SimpleLib: " + Describe(() =>
            Type.GetType("SimpleUser.Caller, SimpleUser", true).GetMethod("Call").Invoke(null, null)) + "\n");
        Response.Write("Assembly.Load SimpleLib 2.0.0.0: " + Describe(() =>
            Assembly.Load("SimpleLib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null").GetName().Version) + "\n");
        Response.Write("Assembly.Load SimpleLib 9.9.9.9: " + Describe(() =>
            Assembly.Load("SimpleLib, Version=9.9.9.9").GetName().Version) + "\n");
    }
</script>
