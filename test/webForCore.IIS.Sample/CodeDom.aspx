<%@ Page Language="C#" %>
<%@ Import Namespace="System.CodeDom" %>
<%@ Import Namespace="System.CodeDom.Compiler" %>
<%@ Import Namespace="System.IO" %>
<%@ Import Namespace="System.Reflection" %>
<script runat="server">
    // System.Web's C# and VB providers (WebFormsForCore.CodeDom.Compiler), which compile every page and App_Code: the
    // generator, the compiler, member generation and the attribute converters, as .NET Framework's
    // Microsoft.CSharp.CSharpCodeProvider and Microsoft.VisualBasic.VBCodeProvider gave them (#57).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Write("cs", new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider());
        Write("vb", new WebFormsForCore.CodeDom.Compiler.VBCodeProvider());
    }

#pragma warning disable 618 // CreateGenerator and CreateCompiler
    private void Write(string name, WebFormsForCore.CodeDom.Compiler.CodeDomProvider provider)
    {
        var field = new CodeMemberField(typeof(int), "count") { Attributes = MemberAttributes.Public | MemberAttributes.Static };
        var writer = new StringWriter();
        provider.GenerateCodeFromMember(field, writer, new CodeGeneratorOptions());
        Response.Write(name + " member: " + writer.ToString().Trim() + "\n");

        Response.Write(name + " member attributes: " + provider.GetConverter(typeof(MemberAttributes)).ConvertToString(MemberAttributes.Assembly) + "\n");
        Response.Write(name + " type attributes: " + provider.GetConverter(typeof(TypeAttributes)).ConvertToString(TypeAttributes.Public) + "\n");

        ICodeGenerator generator = provider.CreateGenerator();
        Response.Write(name + " generator: " + generator.GetType().FullName + ", valid identifier: " + generator.IsValidIdentifier("count") + "\n");
        Response.Write(name + " compiler: " + (provider.CreateCompiler() != null) + "\n");
    }
#pragma warning restore 618
</script>
