#!/usr/bin/env python3
"""Lift Mono's System.Web unit tests (mono/mono, mcs/class/System.Web/Test, MIT) into the fork.

The file list is Mono's own System.Web_test.dll.sources. Stage 1 leaves out:
  - files that use Mono's in-process web test harness (MonoTests.SystemWeb.Framework: WebTest, NunitWeb),
  - Test/mainsoft/ (the harness itself and its third-party parts; licence not yet reviewed),
  - Test/Cas/ (Code Access Security; .NET does not enforce CAS).
Files are copied byte for byte to test/webForCore.System.Web.MonoTests/ at their path below Test/ (helpers below
test-helpers/), then the TRANSFORMS below are applied. The tests run on NUnit 3.14 (the last version with NUnit 2's
classic Assert API in the main namespace); NUnit2Compat.cs supplies [ExpectedException], which NUnit 3 removed.

TARGET_DOTNET and DOT_NET are defined: the fork's System.Web is Microsoft's reference source, so where Mono wrote a
.NET variant of a test, that variant is the one to compile.

Usage: lift_mono_tests.py <MonoRoot> <ForkRoot>
Writes tools/lift/manifests/System.Web.MonoTests.tsv.
"""
import os, re, shutil, subprocess, sys

NAME = "System.Web.MonoTests"
# Web test harness (MonoTests.SystemWeb.Framework, mainsoft's MonoTests.stand_alone) and Code Access Security tests
# (classes deriving from Cas/AspNetHosting*; .NET does not enforce CAS).
HARNESS = re.compile(r"MonoTests\.SystemWeb\.Framework|\bWebTest\b|\bNunitWeb\b|MonoTests\.stand_alone|\bAspNetHosting(Minimal|None)\b")
EXCLUDED_DIRS = ("mainsoft/", "Cas/")
# Not applicable to Microsoft's System.Web: tests of Mono-internal classes, or tests that need a helper defined in a
# harness-using file.
EXCLUDED_FILES = {
    "System.Web.Caching/CacheItemPriorityQueueTest.cs": "tests Mono's internal CacheItemPriorityQueue/CacheItem",
    "System.Web.Util/RequestValidatorTest.cs": "needs BaseFakeHttpWorkerRequest from HttpRequestTest.cs, which uses the web harness",
    "System.Web.Util/MachineKeySectionUtilsTest.cs": "tests Mono's internal MachineKeySectionUtils",
    "System.Web.Util/UrlUtilsTest.cs": "reaches Mono internals through Mono's SystemWebTestShim",
    "System.Web.Hosting/ApplicationHostTest.cs": "inspects the AppDomainSetup of the AppDomain CreateApplicationHost creates; no AppDomains on .NET 11",
}

# NUnit 2 -> NUnit 3: (regex, replacement, expected total). Attribute names only; behaviour is the same.
TRANSFORMS = [
    (r"\bTestFixtureSetUp\b", "OneTimeSetUp", None),
    (r"\bTestFixtureTearDown\b", "OneTimeTearDown", None),
    # Mono's System.Web let its tests override `protected internal` members as `protected internal`; from another
    # assembly (Microsoft's System.Web) C# requires `protected` (CS0507). Every occurrence overrides a System.Web member.
    (r"\b(?:protected\s+internal|internal\s+protected)\s+override\b", "protected override", 17),
    # NUnit 3 renamed Is.StringContaining to Does.Contain (used by test-helpers/NunitHelpers.cs).
    (r"\bIs\.StringContaining\b", "Does.Contain", 1),
]

# Tests corrected to .NET Framework behaviour where Mono's differed: (path, exact old text, new text, expected count).
# Each correction cites the Microsoft reference source (microsoft/referencesource) that decides it; see LIFT.md.
FIXUPS = {
    # HttpContext(null) throws NullReferenceException in Microsoft's constructor (Init -> CookielessHelper ->
    # Request.ClientFilePath with no worker request; referencesource HttpContext.cs, CookielessHelper.cs). Mono allowed it.
    # .NET's way to get a context outside hosting is a real worker request: SimpleWorkerRequest's 5-argument constructor.
    "System.Web/HttpWriter.cs": [
        ("HttpContext context = new HttpContext (null);",
         "HttpContext context = new HttpContext (new global::System.Web.Hosting.SimpleWorkerRequest (\"/\", global::System.IO.Path.GetTempPath (), "
         "\"default.aspx\", \"\", global::System.IO.TextWriter.Null)); // webForCore: was new HttpContext (null), which .NET rejects", 1),
    ],
    # These tests serialise HttpCookie / output-cache entries with BinaryFormatter, which .NET 9+ removed (it throws
    # PlatformNotSupportedException). On .NET the fork's System.Web uses its BinaryFormatter port for the same job
    # (OutputCache.cs, RolePrincipal.cs, BuildResult.cs), so the tests use the port and still check the round trip.
    **{f: [("using System.Runtime.Serialization.Formatters.Binary;",
            "using BinaryFormatter = WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter; // webForCore: the fork's port, as its System.Web uses", 1)]
       for f in ("System.Web/HttpCookieTest.cs", "System.Web.Caching/OutputCacheTest.cs")},
    # ApplicationHost.CreateApplicationHost creates an AppDomain and Microsoft's code requires Windows NT; .NET has no
    # AppDomains, so these tests cannot run. Skipped visibly, like the Web Stack's AppDomain tests.
    "System.Web.Hosting/SimpleWorkerRequestTest.cs": [
        (f"public void {m} ()",
         f'[Ignore ("webForCore: needs ApplicationHost.CreateApplicationHost, i.e. a new AppDomain; not available on .NET 11")]\n\t\tpublic void {m} ()', 1)
        for m in ("AppDomainTests", "AppDomain_MapPath1", "AppDomain_MapPath2", "AppDomain_MapPath3", "AppDomain_MapPath4")
    ],
    # HtmlInputRadioButton.RenderAttributes writes the value with HtmlTextWriter.WriteAttribute(name, value), which
    # calls WriteAttribute(name, value, false /*encode*/) (referencesource HtmlInputRadioButton.cs, HTMLTextWriter.cs:1147);
    # Mono HTML-encoded it.
    "System.Web.UI.HtmlControls/HtmlInputRadioButtonTest.cs": [
        ('Assert.IsTrue (attrs.IndexOf ("value=\\"hola&lt;&amp;\\"") >= 0);',
         'Assert.IsTrue (attrs.IndexOf ("value=\\"hola<&\\"") >= 0); // webForCore: Microsoft writes the value unencoded', 1),
    ],
    # Microsoft initialises _ValidFrom and _ValidUntil with two separate DateTime.Now calls (referencesource
    # HttpClientCertificate.cs:270-271). They were equal on .NET Framework's ~15 ms Windows clock; on a high-resolution
    # clock they are a few ticks apart. The test's intent (both are the construction time, cached) is kept.
    "System.Web/HttpClientCertificateTest.cs": [
        ('Assert.AreEqual (from1, until2, "from-until");',
         'Assert.LessOrEqual (Math.Abs (from1 - until2), TimeSpan.TicksPerMillisecond, "from-until"); // webForCore: two consecutive DateTime.Now reads', 1),
    ],
    # BaseValidator/ValidationSummary set ForeColor = Red only when RenderingCompatibility < 4.0
    # (referencesource BaseValidator.cs:557, ValidationSummary.cs); the default rendering compatibility is 4.0.
    "System.Web.UI.WebControls/ValidationSummaryTest.cs": [
        ('Assert.AreEqual (Color.Red, v.ForeColor, "D3");',
         'Assert.AreEqual (Color.Empty, v.ForeColor, "D3"); // webForCore: no default Red with rendering compatibility 4.0', 1),
        # ValidationSummaryRenderTest never sets ForeColor, so all five expectations (R2-R5, R7) assumed the pre-4.0 Red.
        (r'Assert.AreEqual("<div style=\"color:Red;\">', r'Assert.AreEqual("<div>', 5),
    ],
    # DataBindingCollection.Clear() always calls OnChanged(), and Remove() raises Changed only when the binding is present
    # (referencesource UI/DataBindingCollection.cs). Mono did the opposite in both cases.
    "System.Web.UI/DataBindingCollectionTest.cs": [
        ('a.Clear ();\n\t\t\tAssert.AreEqual (false, changed, "DB2");\n\t\t\t\n\t\t\ta.Remove (b);\n\t\t\tAssert.AreEqual (true, changed, "DB3");',
         'a.Clear ();\n\t\t\tAssert.AreEqual (true, changed, "DB2"); // webForCore: Microsoft\'s Clear() raises Changed\n\t\t\tchanged = false;\n\t\t\t\n'
         '\t\t\ta.Remove (b);\n\t\t\tAssert.AreEqual (false, changed, "DB3"); // webForCore: b is no longer present, so Remove does not raise Changed', 1),
    ],
    "System.Web.UI.WebControls/BaseValidatorTest.cs": [
        ('Assert.AreEqual (Color.Red, p.ForeColor, "D6");',
         'Assert.AreEqual (Color.Empty, p.ForeColor, "D6"); // webForCore: no default Red with rendering compatibility 4.0', 1),
    ],
    "System.Web.UI.WebControls/RangeValidatorTest.cs": [
        (r'Assert.AreEqual("<span style=\"color:Red;\">aw shucks</span>", p.Render(), "R1");',
         'Assert.AreEqual("<span>aw shucks</span>", p.Render(), "R1"); // webForCore: no default Red with rendering compatibility 4.0', 1),
    ],
    # Unit parses the number with SingleConverter and stores (Single) (referencesource Unit.cs), so 0.9 is 0.9f.
    "System.Web.UI.WebControls/UnitTest.cs": [
        ('Assert.AreEqual (0.9d, s1.Value, "B2");',
         'Assert.AreEqual ((double) 0.9f, s1.Value, "B2"); // webForCore: Unit stores the value as Single', 1),
    ],
    # X-AspNet-Version is VersionInfo.SystemWebVersion without its last part (referencesource HttpRuntimeSection.cs),
    # "4.0.30319" on .NET Framework 4.x. Mono's expectation came from Environment.Version, which was 4.0.30319 there.
    "System.Web/HttpResponseTest.cs": [
        ('Assert.AreEqual (Environment.Version.ToString (3), unknown.Value, "#C3");',
         'Assert.AreEqual ("4.0.30319", unknown.Value, "#C3"); // webForCore: the .NET Framework 4.x X-AspNet-Version value', 2),
    ],
    # CharRequiresJavaScriptEncoding (referencesource HttpEncoder.cs:85) also encodes '&' (unless the
    # aspnet:JavaScriptDoNotEncodeAmpersand switch is set), U+0085, U+2028 and U+2029; .NET's HttpUtility does the same.
    "System.Web/HttpUtilityTest.cs": [
        # HtmlDecode with strict decoding conformance (.NET Framework apps targeting 4.5+, the reference) accepts &#000;
        # as U+0000 (referencesource System/net/System/Net/WebUtility.cs:209); Mono kept it (4.0 compat). Only the
        # decoding_pairs expectation changes: the identical encoding_pairs line is an HtmlEncode input.
        ('&#029;&#030;&#031;&#032;",\n\t"&#000;\\x1\\x2\\x3',
         '&#029;&#030;&#031;&#032;",\n\t"\\x0\\x1\\x2\\x3', 1),
        ('&#x1D;&#x1E;&#x1F;&#x20;",\n\t"&#x00;\\x1\\x2\\x3',
         '&#x1D;&#x1E;&#x1F;&#x20;",\n\t"\\x0\\x1\\x2\\x3', 1),  # the same for the hex form &#x00;
        # HtmlEncode with strict encoding conformance (4.5+) writes a valid surrogate pair as &#<scalar>; and replaces a
        # lone surrogate with U+FFFD (WebUtility.cs:123-132); Mono's expectation helper passed it through (4.0 compat).
        ("\t\t\t\t\t\tif (s [i] > 159 && s [i] < 256) {",
         "\t\t\t\t\t\tif (char.IsSurrogate (s [i])) { // webForCore: strict encoding conformance, as .NET Framework 4.5+\n"
         "\t\t\t\t\t\t\tif (char.IsHighSurrogate (s [i]) && i + 1 < len && char.IsLowSurrogate (s [i + 1])) {\n"
         "\t\t\t\t\t\t\t\toutput.Append (\"&#\").Append (char.ConvertToUtf32 (s [i], s [i + 1])).Append (';');\n"
         "\t\t\t\t\t\t\t\ti++;\n"
         "\t\t\t\t\t\t\t} else {\n"
         "\t\t\t\t\t\t\t\toutput.Append ('\\uFFFD');\n"
         "\t\t\t\t\t\t\t}\n"
         "\t\t\t\t\t\t} else if (s [i] > 159 && s [i] < 256) {", 1),
        ("if (c >= 0 && c <= 31 || c == 34 || c == 39 || c == 60 || c == 62 || c == 92) {",
         "if (c >= 0 && c <= 31 || c == 34 || c == 38 || c == 39 || c == 60 || c == 62 || c == 92 || c == 0x85 || c == 0x2028 || c == 0x2029) { // webForCore: & U+0085 U+2028 U+2029, as Microsoft's", 1),
        ("if (c >= 0 && c <= 7 || c == 11 || c >= 14 && c <= 31 || c == 39 || c == 60 || c == 62)",
         "if (c >= 0 && c <= 7 || c == 11 || c >= 14 && c <= 31 || c == 38 || c == 39 || c == 60 || c == 62 || c == 0x85 || c == 0x2028 || c == 0x2029)", 1),
    ],
}

# Whole test methods corrected to .NET Framework behaviour: (path, method signature, replacement including attributes).
# The replacement covers the attribute and comment lines directly above the signature and the method body.
METHODS = {
    "System.Web/HttpWriter.cs": [
        ("public void NullBufferException ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Microsoft's HttpWriter.Write(char[], int, int) throws ArgumentNullException for a null buffer\n"
         "\t\t// (\"Dev10 Bug 507392: Do as TextWriter does\", referencesource HttpWriter.cs:1625); Mono threw ArgumentOutOfRangeException.\n"
         "\t\t[ExpectedException (typeof (ArgumentNullException))]\n"
         "\t\tpublic void NullBufferException ()\n\t\t{\n\t\t\twriter.Write (null, 0, 0);\n\t\t}"),
    ],
    "System.Web.UI/LosFormatterTest.cs": [
        ("public void Serialize_Output_Null ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Mono's NotDotNet test; as its own comment says, Microsoft's LosFormatter calls output.Write directly\n"
         "\t\t// and throws NullReferenceException (referencesource LOSFormatter.cs:127).\n"
         "\t\t[ExpectedException (typeof (NullReferenceException))]\n"
         "\t\tpublic void Serialize_Output_Null ()\n\t\t{\n\t\t\tLosFormatter lf = new LosFormatter ();\n"
         "\t\t\tlf.Serialize ((TextWriter) null, \"test\");\n\t\t}"),
    ],
    "System.Web/HttpResponseTest.cs": [
        ("public void Write_Count_Overflow ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Microsoft's HttpResponseStream.Write throws ArgumentException when offset + count exceeds the buffer\n"
         "\t\t// (referencesource HttpWriter.cs:727); Mono clamped the count.\n"
         "\t\tpublic void Write_Count_Overflow ()\n\t\t{\n\t\t\tbyte [] buffer;\n\n"
         "\t\t\tbuffer = Encoding.UTF8.GetBytes (\"Mono\");\n"
         "\t\t\tAssert.Throws<ArgumentException> (() => out_stream.Write (buffer, 0, buffer.Length + 5), \"#1\");\n"
         "\t\t\tout_stream.Write (buffer, 0, buffer.Length);\n"
         "\t\t\tbuffer = Encoding.UTF8.GetBytes (\"Just Rocks!!\");\n"
         "\t\t\tAssert.Throws<ArgumentException> (() => out_stream.Write (buffer, 5, buffer.Length - 2), \"#2\");\n"
         "\t\t\tout_stream.Write (buffer, 5, buffer.Length - 5);\n"
         "\t\t\tresponse.Flush ();\n\n"
         "\t\t\tstring output = Encoding.UTF8.GetString (worker.data);\n"
         "\t\t\tAssert.AreEqual (\"b\\r\\nMonoRocks!!\\r\\n\", output);\n\t\t}"),
        ("public void Write_Offset_Overflow ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Microsoft checks offset < 0, count < 0, then buffer.Length - offset < count (ArgumentException), and\n"
         "\t\t// returns for count == 0 (referencesource HttpWriter.cs:721-729). So offset == length with count 0 is accepted and\n"
         "\t\t// offset > length throws ArgumentException; Mono threw ArgumentOutOfRangeException for both.\n"
         "\t\tpublic void Write_Offset_Overflow ()\n\t\t{\n\t\t\tbyte [] buffer = new byte [] { 0x0a, 0x1f, 0x2d };\n\n"
         "\t\t\tout_stream.Write (buffer, buffer.Length, 0);\n"
         "\t\t\tAssert.Throws<ArgumentException> (() => out_stream.Write (buffer, buffer.Length + 1, 0), \"#B1\");\n\n"
         "\t\t\tresponse.Flush ();\n\t\t\tAssert.AreEqual (0, worker.data_len);\n\t\t}"),
    ],
    "System.Web.UI.WebControls/UnitTest.cs": [
        ("public void IncorrectConstructor9()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Microsoft's Unit truncates a fractional pixel value instead of throwing\n"
         "\t\t// (\"if (type == UnitType.Pixel) this.value = (int)this.value;\", referencesource Unit.cs).\n"
         "\t\tpublic void IncorrectConstructor9()\n\t\t{\n\t\t\tUnit a = new Unit(\"34.4px\");\n"
         "\t\t\tAssert.AreEqual (UnitType.Pixel, a.Type, \"type\");\n\t\t\tAssert.AreEqual (34d, a.Value, \"value\");\n\t\t}"),
    ],
    "System.Web.Security/FormsIdentityTest.cs": [
        ("public void Null ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: Microsoft's FormsIdentity(FormsAuthenticationTicket) throws ArgumentNullException(\"ticket\")\n"
         "\t\t// (referencesource Security/FormsIdentity.cs:68); Mono accepted null.\n"
         "\t\t[ExpectedException (typeof (ArgumentNullException))]\n"
         "\t\tpublic void Null ()\n\t\t{\n\t\t\tnew FormsIdentity (null);\n\t\t}"),
        ("public void Null_Name ()",
         "\t\t[Test]\n"
         "\t\t// webForCore: see Null (); Microsoft throws in the constructor, before Name is read.\n"
         "\t\t[ExpectedException (typeof (ArgumentNullException))]\n"
         "\t\tpublic void Null_Name ()\n\t\t{\n\t\t\tFormsIdentity identity = new FormsIdentity (null);\n"
         "\t\t\tAssert.IsNull (identity.Name, \"Name\");\n\t\t}"),
    ],
}
XSS = ("\t\t[Test]\n"
       "\t\t// webForCore: Mono's NotDotNet test. .NET Framework (and .NET) do not encode these look-alike characters;\n"
       "\t\t// Microsoft declined to change it (see Mono's comment above). So the encoded text still becomes <script> in windows-1251.\n"
       "\t\tpublic void HtmlEncode_XSS ()\n\t\t{\n"
       "\t\t\tstring problem = \"\\xff1cscript\\xff1e\";  // unicode looks alike <script>\n"
       "\t\t\tEncoding win1251 = Encoding.GetEncoding (\"windows-1251\");\n"
       "\t\t\tstring encoded = {ENCODE} (problem);\n"
       "\t\t\tAssert.AreEqual (problem, encoded, \"not encoded\");\n"
       "\t\t\tbyte [] windata = Encoding.Convert (Encoding.UTF8, win1251, Encoding.UTF8.GetBytes (encoded));\n"
       "\t\t\tAssert.AreEqual (\"<script>\", Encoding.ASCII.GetString (windata), \"<script>\");\n\t\t}")
METHODS["System.Web/HttpUtilityTest.cs"] = [("public void HtmlEncode_XSS ()", XSS.replace("{ENCODE}", "HttpUtility.HtmlEncode"))]
METHODS["System.Web/HttpServerUtilityTest.cs"] = [("public void HtmlEncode_XSS ()", XSS.replace("{ENCODE}", "Server.HtmlEncode"))]


def replace_method(text, signature, replacement):
    """Replace the method whose declaration contains `signature`, with its attribute/comment lines, by `replacement`."""
    lines = text.split("\n")
    hits = [i for i, l in enumerate(lines) if signature in l]
    if len(hits) != 1:
        raise ValueError(f"method {signature!r} found {len(hits)} times")
    start = hits[0]
    while start > 0 and lines[start - 1].strip().startswith(("[", "//")):
        start -= 1
    depth, end, seen = 0, None, False
    for i in range(hits[0], len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        seen = seen or "{" in lines[i]
        if seen and depth == 0:
            end = i
            break
    nl = "\r" if lines[0].endswith("\r") else ""
    new = [l + nl for l in replacement.split("\n")]
    return "\n".join(lines[:start] + new + lines[end + 1:])


COMPAT = '''// webForCore: NUnit 2 compatibility for Mono's System.Web tests (NUnit 3 removed [ExpectedException]).
// Follows the IWrapTestMethod pattern from NUnit 3's documentation: the test passes only if it throws
// exactly the expected exception type (NUnit 2 semantics; derived types do not count).
using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using NUnit.Framework.Internal.Commands;

// Mono annotates some tests with its MonoTODO attribute (internal in System.Web); a no-op stand-in keeps them unchanged.
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class MonoTODOAttribute : Attribute
{
    public MonoTODOAttribute() { }
    public MonoTODOAttribute(string comment) { }
}

// Runs System.Web's module initializer before any test, so the test process has what every .NET Framework process
// using System.Web has (Windows code pages, default DbProviderFactories; see System.Web FrameworkDefaults). Some tests
// call Encoding.GetEncoding("windows-1251") before they touch any System.Web type.
[SetUpFixture]
public sealed class WebForCoreTestEnvironment
{
    [OneTimeSetUp]
    public void InitializeSystemWeb()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(System.Web.HttpContext).Module.ModuleHandle);
    }
}

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ExpectedExceptionAttribute : NUnitAttribute, IWrapTestMethod
    {
        private readonly Type _expectedExceptionType;

        public ExpectedExceptionAttribute(Type type)
        {
            _expectedExceptionType = type;
        }

        public TestCommand Wrap(TestCommand command)
        {
            return new ExpectedExceptionCommand(command, _expectedExceptionType);
        }

        private sealed class ExpectedExceptionCommand : DelegatingTestCommand
        {
            private readonly Type _expectedType;

            public ExpectedExceptionCommand(TestCommand innerCommand, Type expectedType) : base(innerCommand)
            {
                _expectedType = expectedType;
            }

            public override TestResult Execute(TestExecutionContext context)
            {
                Type caughtType = null;
                string caughtMessage = null;
                try
                {
                    innerCommand.Execute(context);
                }
                catch (Exception ex)
                {
                    if (ex is NUnitException && ex.InnerException != null)
                        ex = ex.InnerException;
                    caughtType = ex.GetType();
                    caughtMessage = ex.Message;
                }

                if (caughtType == _expectedType)
                    context.CurrentResult.SetResult(ResultState.Success);
                else if (caughtType != null)
                    context.CurrentResult.SetResult(ResultState.Failure,
                        $"Expected {_expectedType.FullName} but got {caughtType.FullName}: {caughtMessage}");
                else if (context.CurrentResult.ResultState.Status == TestStatus.Failed)
                    { /* keep the assertion failure */ }
                else
                    context.CurrentResult.SetResult(ResultState.Failure,
                        $"Expected {_expectedType.FullName} but no exception was thrown");
                return context.CurrentResult;
            }
        }
    }
}
'''

CSPROJ = '''<Project Sdk="Microsoft.NET.Sdk">

  <!-- Lifted from mono/mono {commit} (mcs/class/System.Web/Test, MIT) by tools/lift/lift_mono_tests.py. See NOTICE. -->
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <!-- The TARGET_DOTNET serialization baselines (OutputCacheTest) were recorded on .NET Framework from a test assembly
         named DotNet4_NUnit_Tests, whose name they contain; Mono's own build calls it System.Web_test. -->
    <AssemblyName>DotNet4_NUnit_Tests</AssemblyName>
    <RootNamespace>MonoTests</RootNamespace>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <!-- Mono's TEST_MCS_FLAGS: -nowarn:219,169,1591 (plus 436,618 from the library flags). -->
    <NoWarn>$(NoWarn);CS0219;CS0169;CS1591;CS0436;CS0618;CS0612;CS0672;SYSLIB0003;SYSLIB0011;SYSLIB0051</NoWarn>
    <DefineConstants>$(DefineConstants);TARGET_DOTNET;DOT_NET</DefineConstants>
  </PropertyGroup>

  <Import Project="..\\..\\src\\webForCore.WebStack.targets" />

  <ItemGroup>
{compiles}
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Web\\WebFormsForCore.Web.csproj" />
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Web.Extensions\\WebFormsForCore.Web.Extensions.csproj" />
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Web.Services\\WebFormsForCore.Web.Services.csproj" />
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Web.ApplicationServices\\WebFormsForCore.Web.ApplicationServices.csproj" />
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Configuration\\WebFormsForCore.Configuration.csproj" />
    <ProjectReference Include="..\\..\\src\\WebFormsForCore.Drawing\\WebFormsForCore.Drawing.csproj" />
    <PackageReference Include="System.Data.SqlClient" Version="4.9.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.11.0" />
  </ItemGroup>
</Project>
'''


def stage1_files(mono):
    web = os.path.join(mono, "mcs", "class", "System.Web")
    out = []
    for line in open(os.path.join(web, "System.Web_test.dll.sources"), encoding="utf-8"):
        rel = line.strip()
        if not rel or rel.startswith("#") or not rel.endswith(".cs"):
            continue
        src = os.path.normpath(os.path.join(web, "Test", rel))
        if not os.path.exists(src) or rel.startswith(EXCLUDED_DIRS) or rel in EXCLUDED_FILES:
            continue
        if rel.endswith("Cas.cs"):  # Code Access Security tests (some derive from other *Cas classes, not AspNetHosting*)
            continue
        if HARNESS.search(open(src, encoding="utf-8-sig", errors="replace").read()):
            continue
        # Below Test/ the path is kept; the shared helpers (../../test-helpers/X.cs) go to test-helpers/X.cs.
        dest = rel if not rel.startswith("../") else "test-helpers/" + os.path.basename(rel)
        out.append((src, dest))
    return out


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    mono, fork = (os.path.abspath(a) for a in sys.argv[1:])
    commit = subprocess.run(["git", "-C", mono, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()
    dest_dir = os.path.join(fork, "test", f"webForCore.{NAME}")
    if os.path.exists(dest_dir):
        shutil.rmtree(dest_dir)
    files = stage1_files(mono)
    manifest, counts, fixed = [], {pat: 0 for pat, _, _ in TRANSFORMS}, []
    for src, dest in files:
        dst = os.path.join(dest_dir, dest)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        raw = open(src, "rb").read()
        text = raw.decode("utf-8-sig", errors="surrogateescape")
        for pat, repl, _ in TRANSFORMS:
            text, n = re.subn(pat, repl, text)
            counts[pat] += n
        for old, new, expected in FIXUPS.get(dest, []):
            found = text.count(old)
            if found != expected:
                sys.exit(f"fixup for {dest} matched {found} times, expected {expected}")
            text = text.replace(old, new)
            fixed.append(f"{dest} ({found})")
        for signature, replacement in METHODS.get(dest, []):
            text = replace_method(text, signature, replacement)
            fixed.append(f"{dest}: {signature.split('(')[0].split()[-1]}")
        bom = raw.startswith(b"\xef\xbb\xbf")
        open(dst, "w", encoding="utf-8-sig" if bom else "utf-8", errors="surrogateescape", newline="").write(text)
        manifest.append((os.path.relpath(src, mono), os.path.relpath(dst, fork)))
    for pat, _, expected in TRANSFORMS:
        if expected is not None and counts[pat] != expected:
            sys.exit(f"transform {pat} matched {counts[pat]} times, expected {expected}")
    open(os.path.join(dest_dir, "NUnit2Compat.cs"), "w", encoding="utf-8").write(COMPAT)
    compiles = "\n".join(f'    <Compile Include="{d}" />' for _, d in sorted(files, key=lambda f: f[1])) + '\n    <Compile Include="NUnit2Compat.cs" />'
    open(os.path.join(dest_dir, f"webForCore.{NAME}.csproj"), "w", encoding="utf-8").write(
        CSPROJ.replace("{commit}", commit).replace("{compiles}", compiles))
    mdir = os.path.join(fork, "tools", "lift", "manifests")
    with open(os.path.join(mdir, f"{NAME}.tsv"), "w", encoding="utf-8") as f:
        f.write(f"# mono/mono {commit}: source path (relative to repo root)\tdestination (relative to fork root)\n")
        f.writelines(f"{a}\t{b}\n" for a, b in sorted(manifest))
    print(f"{NAME}: {len(files)} files from mono {commit}; transforms: " + ", ".join(f"{p}={n}" for p, n in counts.items()))
    for f in fixed:
        print(f"   fixup {f}")
    missing = (set(FIXUPS) | set(METHODS)) - {f.rsplit(" (", 1)[0].split(": ")[0] for f in fixed}
    if missing:
        sys.exit(f"fixups for files that were not copied: {sorted(missing)}")


if __name__ == "__main__":
    main()
