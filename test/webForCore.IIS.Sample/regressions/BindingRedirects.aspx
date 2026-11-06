<%@ Page Language="C#" %>
<%@ Import Namespace="System.Reflection" %>
<script runat="server">
    // Issue #158: Web.config's <runtime><assemblyBinding> <bindingRedirect> to a lower version, and <codeBase>, weren't
    // applied. A customer application carries about 60 redirects, many to a version lower than or equal to the one its
    // references were built against; on .NET Framework fusion applied them from the AppDomain's configuration file,
    // the application's Web.config. The sample deploys StrongLib 1.0.0.0 and StrongUser, built against StrongLib
    // 2.0.0.0; Web.config redirects StrongLib 0.0.0.0-2.5.0.0 and 4.0.0.0 to 1.0.0.0, and names bin_dotnet/codebase,
    // where the build copies CodeBaseLib 1.0.0.0, in a <codeBase>.
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

    // The public key token of webForCore's signing key, which StrongLib and CodeBaseLib are signed with.
    const string Token = "PublicKeyToken=6b8e2037dda5564b";

    static string Version(string name) => Describe(() => Assembly.Load(name).GetName().Version);

    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string root = HttpRuntime.AppDomainAppPath;
        Response.Write("StrongUser calls StrongLib: " + Describe(() =>
            Type.GetType("StrongUser.Caller, StrongUser", true).GetMethod("Call").Invoke(null, null)) + "\n");
        // Down, within a range and to a single version; the same; up (a range's lower end).
        Response.Write("StrongLib 2.0.0.0: " + Version("StrongLib, Version=2.0.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 2.5.0.0: " + Version("StrongLib, Version=2.5.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 4.0.0.0: " + Version("StrongLib, Version=4.0.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 1.0.0.0: " + Version("StrongLib, Version=1.0.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 0.5.0.0: " + Version("StrongLib, Version=0.5.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 2.0.0.0, Type.GetType: " + Describe(() =>
            Type.GetType("StrongLib.Greeting, StrongLib, Version=2.0.0.0, Culture=neutral, " + Token, true).GetProperty("Text").GetValue(null)) + "\n");
        // Not in an oldVersion: not redirected.
        Response.Write("StrongLib 3.0.0.0: " + Version("StrongLib, Version=3.0.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("StrongLib 4.0.0.1: " + Version("StrongLib, Version=4.0.0.1, Culture=neutral, " + Token) + "\n");
        // Another public key token: another assembly, not redirected.
        Response.Write("StrongLib 2.0.0.0, another key: " + Version("StrongLib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef") + "\n");
        // <codeBase version="1.0.0.0" href="bin_dotnet/codebase/CodeBaseLib.dll">, also after a redirect to 1.0.0.0;
        // there is none for 2.0.0.0.
        Response.Write("CodeBaseLib 1.0.0.0: " + Describe(() =>
            Assembly.Load("CodeBaseLib, Version=1.0.0.0, Culture=neutral, " + Token).Location.Substring(root.Length).Replace('\\', '/')) + "\n");
        Response.Write("CodeBaseLib 0.5.0.0: " + Version("CodeBaseLib, Version=0.5.0.0, Culture=neutral, " + Token) + "\n");
        Response.Write("CodeBaseLib 2.0.0.0: " + Version("CodeBaseLib, Version=2.0.0.0, Culture=neutral, " + Token) + "\n");
        // An assembly without a strong name: its <codeBase> must be in the application's folder.
        Response.Write("UnprobedLib, codeBase outside the site: " + Describe(() => Assembly.Load("UnprobedLib")) + "\n");
    }
</script>
