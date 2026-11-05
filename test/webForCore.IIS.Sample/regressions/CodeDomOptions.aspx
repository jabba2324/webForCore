<%@ Page Language="C#" %>
<%@ Import Namespace="System.IO" %>
<script runat="server">
    // Issue #135: WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider, which applications can create themselves (as
    // Composite C1 does), threw ArgumentNullException with GenerateInMemory and no PDB, and NullReferenceException with
    // CompilerOptions null. App_Code/CodeDomMatrix.cs compiles a class with every combination of GenerateInMemory,
    // IncludeDebugInformation, CompilerOptions, OutputAssembly and TempFiles.KeepFiles, and writes what happened.
    protected void Page_Load(object sender, EventArgs e)
    {
        Response.ContentType = "text/plain";
        string folder = Path.Combine(Path.GetTempPath(), "webforcore-codedom-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider();
            Response.Write(CodeDomMatrix.Run((p, s) => provider.CompileAssemblyFromSource(p, s), folder));
        }
        finally
        {
            // On Windows the assemblies loaded from their files stay locked until the process ends.
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
</script>
