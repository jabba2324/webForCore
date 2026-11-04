<%@ Page Language="C#" %>
<script runat="server">
    // Issue #88: the <system.codedom> entries .NET 3.5/4.0 project templates wrote name .NET Framework's own providers,
    // Microsoft.CSharp.CSharpCodeProvider and Microsoft.VisualBasic.VBCodeProvider in System, Version=2.0.0.0 (the
    // sample's Web.config has them). On IIS they compile every page. On .NET those types are System.CodeDom's, which
    // System.Web can't compile with, and every page failed with InvalidCastException (Umbraco 6.2.6). They mean
    // System.Web's own providers, with the entry's provider options. This page compiling is half of the test.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Write("c#");
        Write("vb");
    }

    private void Write(string language)
    {
        var info = WebFormsForCore.CodeDom.Compiler.CodeDomProvider.GetCompilerInfo(language);
        Response.Write(language + " provider: " + info.CodeDomProviderType.FullName + "\n");
        Response.Write(language + " created: " + info.CreateProvider().GetType().FullName + "\n");
        Response.Write(language + " warning level: " + info.CreateDefaultCompilerParameters().WarningLevel + "\n");
    }
</script>
