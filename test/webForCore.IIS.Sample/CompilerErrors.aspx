<%@ Page Language="C#" %>
<%@ Import Namespace="System.CodeDom.Compiler" %>
<%@ Import Namespace="System.IO" %>
<script runat="server">
    // What System.Web's C# and VB providers make of the compiler's output: CompilerErrors with the file, line, column,
    // number and text, warnings, and the exit code. The compiler runs in process through Roslyn's public API, and a
    // command line it leaves to csc (a missing reference) runs in a csc process (#57).
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        Compile("cs", new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider(), null, "",
            "public class A {\n  public void M() {\n    int unused;\n#line 7 \"/site/Page.aspx\"\n    Missing();\n  }\n}\n");
        Compile("cs ok", new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider(), null, "",
            "public class A {\n  public void M() {\n    int unused;\n  }\n}\n");
        Compile("cs missing reference", new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider(), "/nonexistent/Missing.dll", "",
            "public class A { }\n");
        Compile("vb", new WebFormsForCore.CodeDom.Compiler.VBCodeProvider(), null, "/define:_MYTYPE=\\\"Empty\\\"",
            "Public Class A\n  Public Sub M()\n    Dim unused As Integer\n#ExternalSource(\"/site/Page.aspx\", 7)\n    Missing()\n#End ExternalSource\n  End Sub\nEnd Class\n");
    }

    private void Compile(string name, WebFormsForCore.CodeDom.Compiler.CodeDomProvider provider, string reference, string options, string source)
    {
        var directory = Directory.CreateTempSubdirectory("webforcore-compilererrors-").FullName;
        var parameters = new CompilerParameters { WarningLevel = 4, CompilerOptions = options, TempFiles = new TempFileCollection(directory) };
        parameters.OutputAssembly = Path.Combine(directory, "CompilerErrors.dll");
        if (reference != null)
        {
            parameters.ReferencedAssemblies.Add(reference);
        }

        var results = provider.CompileAssemblyFromSource(parameters, source);
        Response.Write(name + ": exit " + results.NativeCompilerReturnValue + ", assembly " + File.Exists(parameters.OutputAssembly) + "\n");
        Directory.Delete(directory, true);
        foreach (CompilerError error in results.Errors)
        {
            var file = string.IsNullOrEmpty(error.FileName) ? "" : Path.GetFileName(error.FileName);
            Response.Write(string.Format("{0}: {1} {2} {3}({4},{5}): {6}\n", name, error.IsWarning ? "warning" : "error",
                error.ErrorNumber, file, error.Line, error.Column, error.ErrorText));
        }
    }
</script>
