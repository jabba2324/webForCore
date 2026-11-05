using System.Net;
using Xunit;

namespace IISGate
{
    // System.Web's C# and VB CodeDom providers, which generate and compile every page and App_Code. They take the
    // generator, the converters and member generation from System.CodeDom's public providers (#57); the strings are
    // what the providers gave when they created System.CodeDom's internal types by reflection.
    [Collection(IISSampleCollection.Name)]
    public class CodeDomTests
    {
        private readonly SampleServer _server;

        public CodeDomTests(SampleServer server)
        {
            _server = server;
        }

        [Fact]
        public async Task ProvidersGenerateAndConvertAsBefore()
        {
            var response = await _server.Client.GetAsync("/CodeDom.aspx");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Contains("cs member: public static int count;\n", body);
            Assert.Contains("cs member attributes: Internal\n", body);
            Assert.Contains("cs type attributes: Public\n", body);
            Assert.Contains("cs generator: Microsoft.CSharp.CSharpCodeGenerator, valid identifier: True\n", body);
            Assert.Contains("cs compiler: True\n", body);

            Assert.Contains("vb member: Public Shared count As Integer\n", body);
            Assert.Contains("vb member attributes: Friend\n", body);
            Assert.Contains("vb type attributes: Public\n", body);
            Assert.Contains("vb generator: Microsoft.VisualBasic.VBCodeGenerator, valid identifier: True\n", body);
            Assert.Contains("vb compiler: True\n", body);
        }

        // CodeDom reads its CompilerErrors from the compiler's text output: csc's and vbc's format, written by the
        // in-process compiler (Roslyn's public API, #57). A missing reference is left to csc, in a process. The
        // expected lines are what the in-process csc and vbc gave before #57.
        [Fact]
        public async Task CompilerErrorsComeFromTheCompilersOutput()
        {
            var response = await _server.Client.GetAsync("/CompilerErrors.aspx");
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.Matches(
                "^cs: exit 1, assembly False\n" +
                @"cs: error CS0103 Page\.aspx\(7,5\): The name 'Missing' does not exist in the current context\n" +
                @"cs: warning CS0168 \w+\.0\.cs\(3,9\): The variable 'unused' is declared but never used\n" +
                "cs ok: exit 0, assembly True\n" +
                @"cs ok: warning CS0168 \w+\.0\.cs\(3,9\): The variable 'unused' is declared but never used\n" +
                "cs missing reference: exit 1, assembly False\n" +
                @"cs missing reference: error CS0006 \(0,0\): Metadata file '/nonexistent/Missing\.dll' could not be found\n" +
                "vb: exit 1, assembly False\n" +
                @"vb: error BC30451 Page\.aspx\(7,0\): 'Missing' is not declared\. It may be inaccessible due to its protection level\.\n" +
                @"vb: warning BC42024 \w+\.0\.vb\(3,0\): Unused local variable: 'unused'\.\n$",
                body);
        }

        // #135: WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider, which applications create themselves (Composite C1),
        // threw ArgumentNullException with GenerateInMemory and no PDB, and NullReferenceException with CompilerOptions
        // null. regressions/CodeDomOptions.aspx compiles with each combination of GenerateInMemory,
        // IncludeDebugInformation, CompilerOptions (null, empty, set), OutputAssembly (set or not) and TempFiles.KeepFiles,
        // and writes the results, the output and the files left; FrameworkData/CodeDomMatrix-net48.txt is what .NET
        // Framework 4.8's Microsoft.CSharp.CSharpCodeProvider gave for the same App_Code/CodeDomMatrix.cs.
        [Fact]
        public async Task CompilerParametersAsOnDotNetFramework()
        {
            var response = await _server.Client.GetAsync("/regressions/CodeDomOptions.aspx");
            string body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);

            string[] expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "FrameworkData", "CodeDomMatrix-net48.txt"));
            string[] actual = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // One difference is kept: the provider is Microsoft's DotNetCompilerPlatform one, which with UseAspNetSettings
            // (the default) puts "/nowarn:1659;1699;1701;612;618" in front of the caller's CompilerOptions, where .NET
            // Framework's CSharpCodeProvider leaves them as they are (docs/NAMESPACES.md).
            Assert.All(actual, line => Assert.Contains(" options=changed ", line));
            var framework = new System.Text.RegularExpressions.Regex(" options=(null|same) ");
            Assert.Equal(expected.Select(line => framework.Replace(line, " options=changed ")), actual);
        }
    }
}
