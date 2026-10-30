#!/usr/bin/env python3
"""Lift ASP.NET Web Stack projects into the fork as real SDK-style projects.

For each project, MSBuild evaluates the source project (a spike-generated csproj for old-style
projects, or Microsoft's own SDK-style csproj) and lists its Compile and EmbeddedResource items.
Every file is copied into src/webForCore.<Name>/ at its original relative path (or its Link
path for shared files), and a csproj is written with explicit items. Each .resx gets an explicit
LogicalName: the base name its Designer.cs passes to ResourceManager when it uses a literal, or
the name Microsoft's build computes (RootNamespace + folder + file name). Assemblies are delay
signed with Microsoft's public key (tools/35MSSharedLib1024.snk), as Microsoft's own build does,
so they keep the PublicKeyToken 31bf3856ad364e35.

Usage: lift_project.py <WebStackRoot> <SpikeGeneratedDir> <ForkRoot> [Name ...]
Writes tools/lift/manifests/<Name>.tsv (source path, destination path) for LIFT.md.
"""
import fnmatch, glob, json, os, re, shutil, subprocess, sys

# Name -> (source csproj relative to WebStack root or spike dir, target framework, folders that map to the project root)
PROJECTS = {
    "System.Net.Http.Formatting": dict(source="webstack:src/System.Net.Http.Formatting.ns2_0/System.Net.Http.Formatting.ns2_0.csproj",
                                       tfm="netstandard2.0", roots=["src/System.Net.Http.Formatting.ns2_0", "src/System.Net.Http.Formatting"]),
    "System.Web.Razor": dict(source="spike:System.Web.Razor", tfm="net11.0", roots=["src/System.Web.Razor"]),
    "System.Web.Http": dict(source="spike:System.Web.Http", tfm="net11.0", roots=["src/System.Web.Http"]),
    "System.Web.Cors": dict(source="spike:System.Web.Cors", tfm="net11.0", roots=["src/System.Web.Cors"]),
    "System.Web.Http.Cors": dict(source="spike:System.Web.Http.Cors", tfm="net11.0", roots=["src/System.Web.Http.Cors"]),
    # Test projects go to test/. Multi-targeting SDK projects are evaluated for their net8.0 item set.
    "Microsoft.TestCommon": dict(source="webstack:test/Microsoft.TestCommon/Microsoft.TestCommon.csproj", tfm="net11.0",
                                 roots=["test/Microsoft.TestCommon"], eval_tfm="net8.0", test=True, runner=False,
                                 # Assert.ThrowsHttpException (MVC tests) needs System.Web, which the fork provides on .NET.
                                 fork_refs=["src/WebFormsForCore.Web/WebFormsForCore.Web.csproj"],
                                 # Built for .NET Framework only by Microsoft; it needs nothing .NET lacks.
                                 compile_extra=["WebUtils.cs"]),
    "System.Net.Http.Formatting.Test": dict(source="webstack:test/System.Net.Http.Formatting.ns2_0.Test/System.Net.Http.Formatting.ns2_0.Test.csproj",
                                            tfm="net11.0", roots=["test/System.Net.Http.Formatting.ns2_0.Test", "test/System.Net.Http.Formatting.Test"],
                                            eval_tfm="net8.0", test=True),
    "System.Web.Razor.Test": dict(source="spike:System.Web.Razor.Test", tfm="net11.0", roots=["test/System.Web.Razor.Test"], test=True),
    "System.Web.Http.Test": dict(source="spike:System.Web.Http.Test", tfm="net11.0", roots=["test/System.Web.Http.Test"], test=True),
    "System.Web.Cors.Test": dict(source="spike:System.Web.Cors.Test", tfm="net11.0", roots=["test/System.Web.Cors.Test"], test=True),
    "System.Web.Http.Cors.Test": dict(source="spike:System.Web.Http.Cors.Test", tfm="net11.0", roots=["test/System.Web.Http.Cors.Test"], test=True),
    # Phase 3: Web API on System.Web and OWIN.
    "System.Web.Http.WebHost": dict(source="spike:System.Web.Http.WebHost", tfm="net11.0", roots=["src/System.Web.Http.WebHost"]),
    "System.Web.Http.Owin": dict(source="spike:System.Web.Http.Owin", tfm="net11.0", roots=["src/System.Web.Http.Owin"]),
    "System.Web.Http.WebHost.Test": dict(source="spike:System.Web.Http.WebHost.Test", tfm="net11.0", roots=["test/System.Web.Http.WebHost.Test"],
                                         test=True),
    # OwinHostIntegrationTest runs Web API on Katana's HttpListener server (unmapped in the spike project).
    "System.Web.Http.Owin.Test": dict(source="spike:System.Web.Http.Owin.Test", tfm="net11.0", roots=["test/System.Web.Http.Owin.Test"], test=True,
                                      extra_refs=["Microsoft.Owin.Hosting", "Microsoft.Owin.Host.HttpListener"]),
    # Katana (aspnet/AspNetKatana, beside the Web Stack checkout). SDK-style net45 projects built with Arcade, so they are
    # not evaluated with MSBuild: the items are the project folder's default globs plus the csproj's explicit items.
    # Phase 4: MVC without view compilation. MVC references WebPages and WebPages.Razor, which reference Deployment.
    "System.Web.WebPages.Deployment": dict(source="spike:System.Web.WebPages.Deployment", tfm="net11.0", roots=["src/System.Web.WebPages.Deployment"]),
    "System.Web.WebPages": dict(source="spike:System.Web.WebPages", tfm="net11.0", roots=["src/System.Web.WebPages"]),
    "System.Web.WebPages.Razor": dict(source="spike:System.Web.WebPages.Razor", tfm="net11.0", roots=["src/System.Web.WebPages.Razor"]),
    "System.Web.Mvc": dict(source="spike:System.Web.Mvc", tfm="net11.0", roots=["src/System.Web.Mvc"],
                           exclude=["LinqBinaryModelBinder.cs"]),  # System.Data.Linq: see the FIXUPS
    "System.Web.Mvc.Test": dict(source="spike:System.Web.Mvc.Test", tfm="net11.0", roots=["test/System.Web.Mvc.Test"], test=True,
                                fork_refs=["src/WebFormsForCore.Serialization.Formatters/WebFormsForCore.Serialization.Formatters.csproj"],
                                exclude=["Test/LinqBinaryModelBinderTest.cs"]),  # System.Data.Linq: see the FIXUPS
    # Phase 5: views. System.Web.Helpers needs System.Web.WebPages (lifted in phase 4).
    "System.Web.Helpers": dict(source="spike:System.Web.Helpers", tfm="net11.0", roots=["src/System.Web.Helpers"]),
    # ICU creates any well-formed culture name; .NET Framework (Windows) threw CultureNotFoundException for unknown ones.
    # PredefinedCulturesOnly restores that (an app setting; see LIFT.md).
    "System.Web.WebPages.Test": dict(source="spike:System.Web.WebPages.Test", tfm="net11.0", roots=["test/System.Web.WebPages.Test"], test=True,
                                     properties={"PredefinedCulturesOnly": "true"},
                                     # HttpAntiForgeryException is TypeForwardedFrom System.Web.Mvc 2.0: reading it back resolves
                                     # System.Web.Mvc and follows its type forward (on .NET Framework, from the GAC).
                                     # (extern alias: its types would clash with WebPages' own).
                                     alias_refs={"System.Web.Mvc": "SystemWebMvc"},
                                     fork_refs=["src/WebFormsForCore.Serialization.Formatters/WebFormsForCore.Serialization.Formatters.csproj"]),
    "System.Web.WebPages.Razor.Test": dict(source="spike:System.Web.WebPages.Razor.Test", tfm="net11.0", roots=["test/System.Web.WebPages.Razor.Test"], test=True,
                                           app_config=True),
    "System.Web.WebPages.Deployment.Test": dict(source="spike:System.Web.WebPages.Deployment.Test", tfm="net11.0",
                                                roots=["test/System.Web.WebPages.Deployment.Test"], test=True),
    "System.Web.Helpers.Test": dict(source="spike:System.Web.Helpers.Test", tfm="net11.0", roots=["test/System.Web.Helpers.Test"], test=True),
    # MVC Futures (the Microsoft.AspNet.Mvc.Futures package).
    # Microsoft builds Futures unsigned (SignAssembly false, as the package on nuget.org is), with MVC's version.
    "Microsoft.Web.Mvc": dict(source="spike:Microsoft.Web.Mvc", tfm="net11.0", roots=["src/Microsoft.Web.Mvc"], unsigned=True,
                              packages=[("System.ServiceModel.Syndication", "11.0.0-rc.3.26522.148")]),  # Atom results
    "Microsoft.Web.Mvc.Test": dict(source="spike:Microsoft.Web.Mvc.Test", tfm="net11.0", roots=["test/Microsoft.Web.Mvc.Test"], test=True,
                                   unsigned=True),
    "Microsoft.Owin": dict(source="katana:src/Microsoft.Owin/Microsoft.Owin.csproj", tfm="net11.0", roots=["src/Microsoft.Owin"]),
    "Microsoft.Owin.Security": dict(source="katana:src/Microsoft.Owin.Security/Microsoft.Owin.Security.csproj", tfm="net11.0",
                                    roots=["src/Microsoft.Owin.Security"],
                                    # DPAPI or its emulation (DpapiDataProtector); internal here, public in System.Configuration.
                                    fork_sources=[("src/WebFormsForCore.Configuration/WebFormsForCore/Security/LocalDataProtection.cs",
                                                   "WebFormsForCore/LocalDataProtection.cs")]),
    "Microsoft.Owin.Security.Cookies": dict(source="katana:src/Microsoft.Owin.Security.Cookies/Microsoft.Owin.Security.Cookies.csproj",
                                            tfm="net11.0", roots=["src/Microsoft.Owin.Security.Cookies"]),
    "Microsoft.Owin.Security.OAuth": dict(source="katana:src/Microsoft.Owin.Security.OAuth/Microsoft.Owin.Security.OAuth.csproj",
                                          tfm="net11.0", roots=["src/Microsoft.Owin.Security.OAuth"]),
    # SignalR's tests configure CORS with it (lift_aspnet.py); it builds on the Web Stack's System.Web.Cors.
    "Microsoft.Owin.Cors": dict(source="katana:src/Microsoft.Owin.Cors/Microsoft.Owin.Cors.csproj", tfm="net11.0",
                                roots=["src/Microsoft.Owin.Cors"]),
    "Microsoft.Owin.Host.SystemWeb": dict(source="katana:src/Microsoft.Owin.Host.SystemWeb/Microsoft.Owin.Host.SystemWeb.csproj",
                                          tfm="net11.0", roots=["src/Microsoft.Owin.Host.SystemWeb"]),
    # The Domain hosting starter runs the app in a new AppDomain over remoting (see FIXUPS).
    "Microsoft.Owin.Hosting": dict(source="katana:src/Microsoft.Owin.Hosting/Microsoft.Owin.Hosting.csproj", tfm="net11.0",
                                   roots=["src/Microsoft.Owin.Hosting"],
                                   exclude=["Starter/DomainHostingStarter.cs", "Starter/DomainHostingStarterAgent.cs"],
                                   # SettingsLoader reads appSettings through ConfigurationManager by reflection. On .NET
                                   # Framework it is always in the GAC; here the app must carry the fork's copy.
                                   fork_refs=["src/WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj"]),
    "Microsoft.Owin.Host.HttpListener": dict(source="katana:src/Microsoft.Owin.Host.HttpListener/Microsoft.Owin.Host.HttpListener.csproj",
                                             tfm="net11.0", roots=["src/Microsoft.Owin.Host.HttpListener"]),
    "Microsoft.Owin.Testing": dict(source="katana:src/Microsoft.Owin.Testing/Microsoft.Owin.Testing.csproj", tfm="net11.0",
                                   roots=["src/Microsoft.Owin.Testing"]),
    "Microsoft.Owin.Tests": dict(source="katana:tests/Microsoft.Owin.Tests/Microsoft.Owin.Tests.csproj", tfm="net11.0",
                                 roots=["tests/Microsoft.Owin.Tests"], test=True),
    "Microsoft.Owin.Host.SystemWeb.Tests": dict(source="katana:tests/Microsoft.Owin.Host.SystemWeb.Tests/Microsoft.Owin.Host.SystemWeb.Tests.csproj",
                                                tfm="net11.0", roots=["tests/Microsoft.Owin.Host.SystemWeb.Tests"], test=True),
    # Only the tests for the lifted middleware: the external sign-in providers (Facebook, Google, Twitter, Microsoft
    # account, OpenID Connect, JWT, Active Directory) are not lifted.
    "Microsoft.Owin.Security.Tests": dict(source="katana:tests/Microsoft.Owin.Security.Tests/Microsoft.Owin.Security.Tests.csproj",
                                          tfm="net11.0", roots=["tests/Microsoft.Owin.Security.Tests"], test=True,
                                          exclude=["Facebook/**", "Google/**", "Twitter/**", "MicrosoftAccount/**",
                                                   "OpenIdConnect/**", "JwtHandlerTests.cs"],
                                          # Linked from Properties/Resources.resx (ResXFileRef), so not items of their own.
                                          files=["katanatest.redmond.corp.microsoft.com.cer", "selfSigned.cer"],
                                          drop_refs={"Microsoft.Owin.Security.ActiveDirectory", "Microsoft.Owin.Security.Facebook",
                                                     "Microsoft.Owin.Security.Google", "Microsoft.Owin.Security.Jwt",
                                                     "Microsoft.Owin.Security.MicrosoftAccount", "Microsoft.Owin.Security.OpenIdConnect",
                                                     "Microsoft.Owin.Security.Twitter"}),
}
# Katana: version and assembly attributes that Arcade generates (read from Microsoft.Owin 4.2.3 on nuget.org).
KATANA_VERSION, KATANA_FILE_VERSION = "4.2.3.0", "4.200.325.35202"
# .NET Framework references in Katana projects -> the fork's projects or .NET packages.
KATANA_REFERENCES = {
    "System.Web": "src/WebFormsForCore.Web/WebFormsForCore.Web.csproj",
    "System.Configuration": "src/WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj",
    "System.Security": ("System.Security.Cryptography.ProtectedData", "11.0.0-rc.3.26522.148"),
}
KATANA_BUILTIN = {"System.Net.Http", "System.Xml", "System.Xml.Linq", "System.Runtime.Serialization", "System.IdentityModel"}
# Katana's tests run on Arcade's xunit; the lifted tests use the same xunit as the Web Stack's.
KATANA_TEST_PACKAGES = [("xunit", "2.9.3")]
# Spike projects reference Katana from NuGet; the lifted projects reference the lifted Katana instead.
PACKAGE_TO_PROJECT = {"Microsoft.Owin": "Microsoft.Owin", "Microsoft.AspNet.Cors": "System.Web.Cors"}
# Test runner packages: Microsoft's (Test SDK 16.11, runner 2.4.5) predate .NET 11.
PACKAGE_VERSION_OVERRIDES = {"Microsoft.NET.Test.Sdk": "18.11.0", "xunit.runner.visualstudio": "3.1.5"}
DROP_PACKAGES = {"XunitXml.TestLogger"}
# Microsoft builds and tests the Web Stack on Windows, where its `* text` .gitattributes checks files out with CRLF.
# Multi-line string literals (in product code and tests) and Razor test baselines depend on that, so every text
# file is written with CRLF, as a Windows checkout has it. Binary types are those the Web Stack's .gitattributes marks binary.
BINARY_EXTENSIONS = {".bmp", ".dll", ".gif", ".jpg", ".png", ".snk", ".ico", ".pfx", ".zip"}

# Source edits applied after copying: (relative path, exact old text, new text). Each must match exactly once.
# Written with "\n"; matched against the CRLF file with "\r\n".
# Test members compiled out: (relative path, text that starts the member's declaration line, reason). The member,
# with its attributes, goes under #if NETFRAMEWORK (never defined here). Applied after FIXUPS.
MEMBERS_OUT = {}
# Edit tables filled in below (see member_span, edit_member, wrap_member and lift()).
TRANSFORMS, RE_TRANSFORMS, MEMBER_EDITS = {}, {}, {}
FIXUPS = {
    "System.Web.Http.Test": [
        ("Controllers/AuthenticationFilterResultTests.cs", "CancellationToken cancellationToken;\n",
         "CancellationToken cancellationToken = default; // webForCore: .NET reference assemblies expose the struct's private field (CS0165)\n", 3),
    ],
    # webForCore: ValidateSetsMemberNameProperty_* run in AppDomainUtils.RunInSeparateAppDomain (a PseudoAppDomain-style
    # collectible load context on .NET, tools/lift/additions/Microsoft.TestCommon/AppDomainUtils.cs), unchanged.
}

# .NET Framework 4.8 behaviour (decided 30 Oct 2026) where .NET 11 differs inside Web API's own code paths.
# Single/Double.TryParse return false on overflow on .NET Framework (Number.cs: "if (!IsFinite) ... return false" unless
# the input is the culture's infinity symbol); since .NET Core 3.0 they return true with +/-Infinity. Restore 4.8.
for _t, _v in (("Float", "Single"), ("Double", "Double")):
    FIXUPS.setdefault("System.Web.Http", []).append((
        f"Routing/Constraints/{_t}RouteConstraint.cs",
        f"                return {_v}.TryParse(valueString, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);\n",
        f"                // webForCore: .NET Framework's TryParse fails on overflow; .NET's returns Infinity. Accept an infinite\n"
        f"                // result only for the invariant infinity symbols, as .NET Framework did.\n"
        f"                if (!{_v}.TryParse(valueString, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result))\n"
        f"                {{\n                    return false;\n                }}\n"
        f"                string trimmed = valueString.Trim();\n"
        f"                return !{_v}.IsInfinity(result)\n"
        f"                    || trimmed == NumberFormatInfo.InvariantInfo.PositiveInfinitySymbol\n"
        f"                    || trimmed == NumberFormatInfo.InvariantInfo.NegativeInfinitySymbol;\n", 1))

# The same constraints are compiled into System.Web.Mvc (src/Common).
FIXUPS["System.Web.Mvc"] = [f for f in FIXUPS["System.Web.Http"] if f[0].startswith("Routing/Constraints/")]

# Accepted platform differences (decided 30 Oct 2026): the exception comes from the .NET BCL, not from Web API, and the
# test is corrected to .NET's behaviour. Listed in LIFT.md ("Accepted platform differences").
PARAM = 'webForCore: .NET formats ArgumentException messages as "<message> (Parameter \'<name>\')"'
FIXUPS["System.Web.Http.Test"] += [
    ("ModelBinding/Binders/MutableObjectModelBinderTest.cs",
     'Assert.Equal("Date of death can\'t be before date of birth." + Environment.NewLine\n'
     '                       + "Parameter name: value",',
     f'// {PARAM}\n            Assert.Equal("Date of death can\'t be before date of birth. (Parameter \'value\')",', 1),
    ("ModelBinding/Binders/MutableObjectModelBinderTest.cs",
     '"This is a different exception." + Environment.NewLine + "Parameter name: value",',
     f'"This is a different exception. (Parameter \'value\')", // {PARAM}', 1),
    ("Filters/HttpFilterCollectionTest.cs",
     '"The parameter \'filters\' cannot contain a null element." + Environment.NewLine +\n'
     '                "Parameter name: filters");',
     '"The parameter \'filters\' cannot contain a null element. (Parameter \'filters\')"); // ' + PARAM, 1),
    ("Services/DefaultServicesTests.cs",
     '(typeof(ValueProviderFactory), -1, new[] { new Mock<ValueProviderFactory>().Object }),\n                "index",\n'
     '                "Index was out of range. Must be non-negative and less than the size of the collection.");',
     '(typeof(ValueProviderFactory), -1, new[] { new Mock<ValueProviderFactory>().Object }),\n                "index",\n'
     '                "Index was out of range. Must be non-negative and less than or equal to the size of the collection."); '
     '// webForCore: .NET\'s List<T>.InsertRange message', 1),
    ("Controllers/ReflectedHttpActionDescriptorTest.cs",
     "returned an instance of 'System.Threading.Tasks.Task`1[[System.Threading.Tasks.Task, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]'.",
     "returned an instance of '\" + typeof(Task<Task>).FullName + \"'.", 1),  # mscorlib is System.Private.CoreLib on .NET
    ("Controllers/ReflectedHttpActionDescriptorTest.cs",
     "await Assert.ThrowsAsync<InvalidOperationException>(\n                 () => actionDescriptor.ExecuteAsync(_context, _arguments, CancellationToken.None),\n"
     "                 \"The method 'WrappedTaskReturningMethod'",
     "// webForCore: the type's assembly-qualified name names System.Private.CoreLib on .NET, mscorlib on .NET Framework.\n"
     "            await Assert.ThrowsAsync<InvalidOperationException>(\n                 () => actionDescriptor.ExecuteAsync(_context, _arguments, CancellationToken.None),\n"
     "                 \"The method 'WrappedTaskReturningMethod'", 1),
    ("Dispatcher/DefaultHttpControllerSelectorTest.cs",
     "which is not supported.\\r\\n\\r\\nThe request for 'Sample' has found the following matching controllers:\",",
     "which is not supported.\" + Environment.NewLine + Environment.NewLine + \"The request for 'Sample' has found the following matching controllers:\", "
     "// webForCore: the message is built with Environment.NewLine", 1),
    ("HttpRequestMessageExtensionsTest.cs",
     'Assert.ThrowsArgument(() => request.CreateResponse(HttpStatusCode.OK, CreateValue(), (string)null), "mediaType");',
     '// webForCore: .NET\'s MediaTypeHeaderValue(string) throws ArgumentNullException for null (.NET Framework: ArgumentException).\n'
     '            Assert.ThrowsArgumentNull(() => request.CreateResponse(HttpStatusCode.OK, CreateValue(), (string)null), "mediaType");', 1),
    # .NET's System.Uri has a private parameterless constructor (.NET Framework's had none), and Expression.New uses it.
    # The tests want a class without a default constructor; StringContent has none on either platform.
    ("Internal/TypeActivatorTest.cs", "        [InlineData(typeof(Uri))]\n",
     "        [InlineData(typeof(StringContent))] // webForCore: .NET's Uri has a private parameterless constructor\n", 1),
    ("Internal/TypeActivatorTest.cs",
     "            // Uri doesn't have a default ctor\n            Assert.ThrowsArgument(() => TypeActivator.Create<Uri>(), paramName: null);",
     "            // StringContent doesn't have a default ctor (webForCore: .NET's Uri has a private one, which Expression.New uses)\n"
     "            Assert.ThrowsArgument(() => TypeActivator.Create<StringContent>(), paramName: null);", 1),
]

# Newline policy (decided 30 Oct 2026): product code keeps the platform newline (\n on Linux); only tests change.
# Tests that build parser input with Environment.NewLine but hard-code \r\n positions get "\r\n" input (as on Microsoft's
# Windows CI); comparisons of product output are newline-agnostic.
NL = '// webForCore: the platform newline is \\n on Linux; '
FIXUPS["System.Web.Razor.Test"] = [
    ("CodeCompileUnitExtensions.cs", "using (StringWriter writer = new StringWriter(output))",
     'using (StringWriter writer = new StringWriter(output) { NewLine = "\\r\\n" }) ' + NL + "CodeDom writes as on Windows", 1),
    ("Generator/RazorCodeGeneratorTest.cs", "using (StringWriter writer = new StringWriter(output))",
     'using (StringWriter writer = new StringWriter(output) { NewLine = "\\r\\n" }) ' + NL + "CodeDom writes as on Windows", 1),
    ("Generator/RazorCodeGeneratorTest.cs", "Assert.Equal(expectedOutput, textOutput);",
     "Assert.Equal(MiscUtils.NormalizeGeneratedCode(expectedOutput), MiscUtils.NormalizeGeneratedCode(textOutput));", 1),
    ("Editor/RazorEditorParserTest.cs",
     "SimpleCSHTMLDocumentGenerated.ReadAllText(),\n                        MiscUtils.StripRuntimeVersion(generatedCode));",
     "MiscUtils.NormalizeGeneratedCode(SimpleCSHTMLDocumentGenerated.ReadAllText()),\n"
     "                        MiscUtils.NormalizeGeneratedCode(generatedCode));", 1),
    ("Utils/MiscUtils.cs", 'return Regex.Replace(s, @"Runtime Version:[\\d.]*", "Runtime Version:N.N.NNNNN.N");\n        }',
     "// webForCore: .NET's System.CodeDom no longer writes the \"Runtime Version:\" header line that .NET Framework's did,\n"
     "            // so drop the line itself (from generated code and baselines alike) rather than only its version number.\n"
     '            return Regex.Replace(s, @"(//|\')\\s*Runtime Version:[^\\r\\n]*\\r?\\n", "");\n        }\n\n'
     "        // webForCore: Razor's generator writes Environment.NewLine into some snippets (\\n on Linux) while the baselines were\n"
     "        // recorded on Windows (\\r\\n). Product output keeps the platform newline, so compare generated code newline-agnostically.\n"
     "        public static string NormalizeGeneratedCode(string s)\n        {\n"
     '            return StripRuntimeVersion(s).Replace("\\r\\n", "\\n");\n        }', 1),
]
FIXUPS["Microsoft.TestCommon"] = [
    ("ExceptionAssertions.cs", "#if !NETCOREAPP\n        /// <summary>\n        /// Verifies that the code throws an HttpException",
     "// webForCore: the fork provides System.Web on .NET, so ThrowsHttpException is built here too.\n"
     "#if true\n        /// <summary>\n        /// Verifies that the code throws an HttpException", 1),
    ("ExceptionAssertions.cs",
     "            if (expectedMessage != null)\n            {\n                if (!partialMatch)\n                {\n"
     "                    Equal(expectedMessage, exception.Message);\n                }\n                else\n                {\n"
     "                    Contains(expectedMessage, exception.Message);\n                }\n            }",
     "            if (expectedMessage != null)\n            {\n"
     "                // webForCore: .NET builds messages with Environment.NewLine (\\n on Linux) where the expected messages were\n"
     "                // written for Windows (\\r\\n); compare newline-agnostically.\n"
     '                expectedMessage = expectedMessage.Replace("\\r\\n", "\\n");\n'
     '                string actualMessage = exception.Message?.Replace("\\r\\n", "\\n");\n'
     "                if (!partialMatch)\n                {\n                    Equal(expectedMessage, actualMessage);\n                }\n"
     "                else\n                {\n                    Contains(expectedMessage, actualMessage);\n                }\n            }", 1),
]
FIXUPS["System.Net.Http.Formatting.Test"] = [
    (f"Formatting/{f}", 'serializedString.Contains("\\r\\n")',
     'serializedString.Contains("\\n") /* webForCore: indentation uses the platform newline */', n)
    for f, n in (("DataContractJsonMediaTypeFormatterTests.cs", 1), ("JsonMediaTypeFormatterTests.cs", 2),
                 ("XmlMediaTypeFormatterTests.cs", 2), ("XmlSerializerMediaTypeFormatterTests.cs", 2))
]
# .NET Framework's Path.GetFullPath rejected whitespace-only paths (ArgumentException "The path is not of a legal form.",
# no parameter name); .NET's accepts them on Linux and resolves them under the current directory, so uploads would be
# written to "<cwd>/  ". Restore 4.8.
FIXUPS["System.Net.Http.Formatting"] = [
    ("MultipartFileStreamProvider.cs", "            _rootPath = Path.GetFullPath(rootPath);\n",
     "            // webForCore: .NET Framework's Path.GetFullPath rejected whitespace-only paths; .NET's accepts them on Linux.\n"
     "            if (rootPath.Trim().Length == 0)\n            {\n"
     '                throw new ArgumentException("The path is not of a legal form.");\n            }\n\n'
     "            _rootPath = Path.GetFullPath(rootPath);\n", 1),
]
FIXUPS["System.Net.Http.Formatting.Test"] += [
    # Accepted platform difference: .NET's ContentDispositionHeaderValue.FileName returns the value unquoted
    # (.NET Framework's returned the quoted token). Web API unquotes FileName itself (FormattingUtilities.UnquoteToken),
    # so its own providers behave the same; application code reading FileName directly sees the difference.
    ("HttpContentMultipartExtensionsTests.cs",
     'Assert.Equal(String.Format("\\"N{0}\\"", cnt), content.Headers.ContentDisposition.FileName);',
     'Assert.Equal(String.Format("N{0}", cnt), content.Headers.ContentDisposition.FileName); '
     "// webForCore: .NET's ContentDisposition.FileName is unquoted", 1),
    # Accepted platform difference: since .NET Core 3.0 the UTF-8 decoder replaces each byte of an invalid sequence
    # (Unicode "maximal subpart" practice); each lone surrogate's 3 bytes become 3 U+FFFD (2 on .NET Core 2.1).
    ("Formatting/JsonNetSerializationTest.cs",
     '#if NETCOREAPP // Json.NET uses its regular invalid Unicode character on .NET Core 2.1; \'?\' elsewhere.\n'
     '        [InlineData("\\udc00\\ud800\\ud800", "\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd")]',
     '#if NETCOREAPP // Json.NET uses its regular invalid Unicode character on .NET Core 2.1; \'?\' elsewhere.\n'
     "        // webForCore: .NET Core 3.0+'s UTF-8 decoder replaces each byte of the invalid sequences (3 per surrogate).\n"
     '        [InlineData("\\udc00\\ud800\\ud800", "\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd\\ufffd")]', 1),
    # Accepted platform difference: .NET's HttpContent.ReadAsStreamAsync serializes into an internal buffer stream and
    # returns a different stream over that buffer, so the formatter no longer receives the instance that
    # SerializeToStreamAsync wrote to. The test still checks the formatter is called once with the content.
    ("HttpContentExtensionsTest.cs",
     "            _formatterMock.Verify(f => f.ReadFromStreamAsync(typeof(string), contentStream, content, null), Times.Once());",
     "            // webForCore: .NET reads back through a different stream over its internal buffer.\n"
     "            Assert.NotNull(contentStream);\n"
     "            _formatterMock.Verify(f => f.ReadFromStreamAsync(typeof(string), It.IsAny<Stream>(), content, null), Times.Once());", 1),
]
# HttpResponseMessage.Content is never null on .NET (EmptyContent); the hosts and batching read null as "no body".
# ResponseContent.cs (tools/lift/additions) restores the .NET Framework reading.
RC = "// webForCore: ResponseContent.Get treats .NET's EmptyContent as no content, as null was on .NET Framework.\n"
FIXUPS["System.Web.Http.WebHost"] = [
    ("HttpControllerHandler.cs", "            if (response.Content != null)\n            {\n                await WriteResponseContentAsync(",
     "            " + RC + "            if (ResponseContent.Get(response) != null)\n            {\n                await WriteResponseContentAsync(", 1),
    ("HttpControllerHandler.cs", "            if (errorResponse.Content == null)\n",
     "            " + RC + "            if (ResponseContent.Get(errorResponse) == null)\n", 1),
    ("HttpControllerHandler.cs", "            HttpContent content = response.Content;\n            bool isTransferEncodingChunked",
     "            " + RC + "            HttpContent content = ResponseContent.Get(response);\n            bool isTransferEncodingChunked", 1),
    ("WebHostExceptionHandler.cs", "            HttpContent responseContent = response.Content;\n",
     "            " + RC + "            HttpContent responseContent = ResponseContent.Get(response);\n", 1),
    ("WebHostBufferPolicySelector.cs", "            HttpContent content = response.Content;\n",
     "            " + RC + "            HttpContent content = ResponseContent.Get(response);\n", 1),
]
FIXUPS["System.Web.Http.WebHost"] += [
    # .NET's StreamContent copies with Stream.CopyToAsync, which NonOwnedStream forwards to the inner stream, skipping the
    # end-of-stream switch to Request.InputStream. .NET Framework's StreamContent copied through Read/BeginRead.
    ("SeekableBufferedRequestStream.cs", "        public override int EndRead(IAsyncResult asyncResult)\n",
     "        // webForCore: .NET's StreamContent copies with CopyToAsync, which NonOwnedStream forwards to the inner stream;\n"
     "        // that skips the switch to Request.InputStream once the body is read to the end (SwapToSeekableStream).\n"
     "        // .NET Framework's StreamContent read through this stream, so copy through ReadAsync here.\n"
     "        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)\n"
     "        {\n"
     "            byte[] buffer = new byte[bufferSize];\n"
     "            int bytesRead;\n"
     "            while ((bytesRead = await ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)\n"
     "            {\n"
     "                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);\n"
     "            }\n"
     "        }\n"
     "\n"
     "        public override int EndRead(IAsyncResult asyncResult)\n", 1),
]
FIXUPS["System.Web.Http.WebHost.Test"] = [
    ("SeekableBufferedRequestStreamTest.cs",
     "                \"'SeekOrigin'.\" + Environment.NewLine +\n                \"Parameter name: origin\";\n",
     "                \"'SeekOrigin'. (Parameter 'origin')\"; // " + PARAM + "\n", 1),
    # .NET's async stack traces name the method itself, not the state machine's MoveNext.
    ("HttpControllerHandlerTest.cs",
     "            Assert.Contains(MethodInfo.GetCurrentMethod().Name, innerError[\"StackTrace\"].ToString());\n",
     "            // webForCore: .NET's async stack traces show the async method, not its state machine's MoveNext.\n"
     "            Assert.Contains(nameof(CopyResponseAsync_IfHandlerIsDefault_Returns_Error_Response_When_Formatter_Write_Task_Faults), innerError[\"StackTrace\"].ToString());\n", 1),
]
FIXUPS["System.Web.Http.WebHost.Test"] += [
    ("WebHostExceptionHandlerTests.cs", "                    Assert.Null(response.Content);\n",
     "                    // webForCore: HttpResponseMessage.Content is never null on .NET (EmptyContent); see ResponseContent.cs.\n"
     "                    Assert.Null(ResponseContent.Get(response));\n", 1),
]
# The test certificate is deliberately degenerate (empty OIDs, long-form lengths). Windows CryptoAPI parses it; .NET's
# DER reader outside Windows rejects it. The test checks RawData round-trips, so it gets a minimal valid certificate.
FIXUPS["System.Web.Http.WebHost.Test"] += [
    ("WebHostHttpRequestContextTests.cs",
     "        private const string Base64Certificate = \"MIFRMIFHAgEAMAIGADAJMQcwBQYAEwFhMB4XDTEzMDkxMDE5NTQ0OVoXDTM5MTIzMT\" +\n"
     "            \"IzNTk1OVowCTEHMAUGABMBYTCBBzACBgADgQAwAgYAA4EA\";\n",
     "        // webForCore: the original certificate (empty OIDs, long-form lengths) is rejected by .NET's DER reader outside\n"
     "        // Windows. A minimal valid self-signed certificate (EC P-256, CN=a) instead.\n"
     "        private const string Base64Certificate = \"MIIBbzCCARWgAwIBAgIULQFNF23+7TPiEStNMnxMQ6Otym0wCgYIKoZIzj0EAwIwDDEKMAgGA1UEAwwBYTAgFw0yNjEwMzAwODQzNDhaGA8yMTI2MTAwNjA4NDM0OFowDDEKMAgGA1UEAwwBYTBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABNmg3PRJ4QrPWfFOwO75KQ6urPsFmo4HLACfGFhnkxfYRwL2+tlrneSBeas86bKPToTiQjSzFZXqvd8XLJfbCxOjUzBRMB0GA1UdDgQWBBSwfzS+wbWn4yuSnWhj6jh0JSevSjAfBgNVHSMEGDAWgBSwfzS+wbWn4yuSnWhj6jh0JSevSjAPBgNVHRMBAf8EBTADAQH/MAoGCCqGSM49BAMCA0gAMEUCIQCC8S4//SQ9RslWdM6VMnsdB1bGDYgmRr6BUGGYQ+qf/QIgad91lsbvNKUT0GHP5tZxEjgHI70RbcbLd5EClcSJgqg=\";\n", 1),
]
FIXUPS["System.Web.Http.Owin"] = [
    ("HttpMessageHandlerAdapter.cs", "                    if (response.Content == null\n",
     "                    " + RC + "                    if (ResponseContent.Get(response) == null\n", 1),
    ("HttpMessageHandlerAdapter.cs", "                        else if (response.Content != null)\n",
     "                        else if (ResponseContent.Get(response) != null)\n", 1),
    ("HttpMessageHandlerAdapter.cs", "            HttpContent content = response.Content;\n            bool isTransferEncodingChunked",
     "            " + RC + "            HttpContent content = ResponseContent.Get(response);\n            bool isTransferEncodingChunked", 1),
    ("HttpMessageHandlerAdapter.cs", "            HttpContent responseContent = response.Content;\n",
     "            " + RC + "            HttpContent responseContent = ResponseContent.Get(response);\n", 1),
    ("OwinBufferPolicySelector.cs", "            HttpContent content = response.Content;\n",
     "            " + RC + "            HttpContent content = ResponseContent.Get(response);\n", 1),
]
FIXUPS["System.Net.Http.Formatting"] += [
    ("HttpMessageContent.cs",
     "            get { return HttpRequestMessage != null ? HttpRequestMessage.Content : HttpResponseMessage.Content; }\n",
     "            " + RC +
     "            get { return HttpRequestMessage != null ? HttpRequestMessage.Content : System.Net.Http.Internal.ResponseContent.Get(HttpResponseMessage); }\n", 1),
    ("HttpMessageContent.cs", "                content = HttpResponseMessage.Content;\n",
     "                " + RC + "                content = System.Net.Http.Internal.ResponseContent.Get(HttpResponseMessage);\n", 1),
]

# Deployment: GetIncompatibleDependencies (tooling API) loads the bin assemblies in a throwaway AppDomain.
FIXUPS["System.Web.WebPages.Deployment"] = [
    ("AppDomainHelper.cs",
     '            AppDomain appDomain = null;\n'
     '            try\n'
     '            {\n'
     '                var appDomainSetup = new AppDomainSetup\n'
     '                {\n'
     '                    ApplicationBase = appPath,\n'
     '                    ConfigurationFile = configPath,\n'
     '                    PrivateBinPath = binDirectory,\n'
     '                };\n'
     '                appDomain = AppDomain.CreateDomain(typeof(AppDomainHelper).Namespace, AppDomain.CurrentDomain.Evidence, appDomainSetup);\n'
     '\n'
     '                var type = typeof(RemoteAssemblyLoader);\n'
     '                var instance = (RemoteAssemblyLoader)appDomain.CreateInstanceAndUnwrap(type.Assembly.FullName, type.FullName);\n'
     '\n'
     '                return Directory.EnumerateFiles(binDirectory, "*.dll")\n'
     '                    .ToDictionary(assemblyPath => assemblyPath,\n'
     '                                  assemblyPath => instance.GetReferences(assemblyPath));\n'
     '            }\n'
     '            finally\n'
     '            {\n'
     '                if (appDomain != null)\n'
     '                {\n'
     '                    AppDomain.Unload(appDomain);\n'
     '                }\n'
     '            }\n'
     '        }\n'
     '\n',
     "            // webForCore: .NET has no AppDomains (CreateDomain, AppDomainSetup.PrivateBinPath, Evidence). A collectible\n"
     "            // AssemblyLoadContext does the same job: the bin assemblies load there, references resolve from the bin\n"
     "            // directory and otherwise as in the app (the default context), and it is unloaded afterwards.\n"
     "            var loadContext = new System.Runtime.Loader.AssemblyLoadContext(typeof(AppDomainHelper).Namespace, isCollectible: true);\n"
     "            loadContext.Resolving += (context, name) =>\n"
     "            {\n"
     "                string candidate = Path.Combine(binDirectory, name.Name + \".dll\");\n"
     "                return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;\n"
     "            };\n"
     "            try\n"
     "            {\n"
     "                return Directory.EnumerateFiles(binDirectory, \"*.dll\")\n"
     "                    .ToDictionary(assemblyPath => assemblyPath,\n"
     "                                  assemblyPath => GetReferences(loadContext, assemblyPath));\n"
     "            }\n"
     "            finally\n"
     "            {\n"
     "                loadContext.Unload();\n"
     "            }\n"
     "        }\n"
     "\n"
     "        private static IEnumerable<string> GetReferences(System.Runtime.Loader.AssemblyLoadContext loadContext, string assemblyPath)\n"
     "        {\n"
     "            var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));\n"
     "            return assembly.GetReferencedAssemblies()\n"
     "                .Select(asmName => loadContext.LoadFromAssemblyName(asmName).FullName)\n"
     "                .Concat(new[] { assembly.FullName })\n"
     "                .ToArray();\n"
     "        }\n"
     "\n", 1),
]

# System.Data.Linq (LINQ to SQL) does not exist on .NET, so no app can pass MVC a System.Data.Linq.Binary: its
# support is compiled out (NETFRAMEWORK is never defined here), and the public LinqBinaryModelBinder is not built.
LINQ_NOTE = "// webForCore: System.Data.Linq (LINQ to SQL) does not exist on .NET; its Binary support is compiled out.\n"
USING_LINQ = ("using System.Data.Linq;\n", LINQ_NOTE + "#if NETFRAMEWORK\nusing System.Data.Linq;\n#endif\n", 1)
BINARY_TO_ARRAY = ("            Binary binaryValue = value as Binary;\n"
                   "            if (binaryValue != null)\n"
                   "            {\n"
                   "                value = binaryValue.ToArray();\n"
                   "            }\n",
                   "#if NETFRAMEWORK\n"
                   "            Binary binaryValue = value as Binary;\n"
                   "            if (binaryValue != null)\n"
                   "            {\n"
                   "                value = binaryValue.ToArray();\n"
                   "            }\n"
                   "#endif\n", 1)
FIXUPS["System.Web.WebPages"] = [("Html/HtmlHelper.Input.cs",) + USING_LINQ, ("Html/HtmlHelper.Input.cs",) + BINARY_TO_ARRAY,
    # SHA256Cng is Windows CNG; any SHA-256 implementation gives the same hash (anti-forgery tokens).
    ("Helpers/CryptoUtil.cs", "                    using (SHA256Cng sha256 = new SHA256Cng())\n",
     "                    // webForCore: SHA256Cng is Windows CNG only; SHA-256 is the same hash from any implementation.\n"
     "                    using (SHA256 sha256 = SHA256.Create())\n", 1)]
FIXUPS["System.Web.Mvc"] += [
    ("Html/InputExtensions.cs",) + USING_LINQ, ("Html/InputExtensions.cs",) + BINARY_TO_ARRAY,
    ("Html/DefaultEditorTemplates.cs",) + USING_LINQ,
    ("Html/DefaultEditorTemplates.cs",
     "            Binary modelAsBinary = model as Binary;\n"
     "            if (modelAsBinary != null)\n"
     "            {\n"
     "                model = Convert.ToBase64String(modelAsBinary.ToArray());\n"
     "            }\n"
     "            else\n",
     "#if NETFRAMEWORK\n"
     "            Binary modelAsBinary = model as Binary;\n"
     "            if (modelAsBinary != null)\n"
     "            {\n"
     "                model = Convert.ToBase64String(modelAsBinary.ToArray());\n"
     "            }\n"
     "            else\n"
     "#endif\n", 1),
    ("OutputCacheAttribute.cs", "            using (SHA256Cng sha = new SHA256Cng())\n",
     "            // webForCore: SHA256Cng is Windows CNG only; SHA-256 is the same hash from any implementation.\n"
     "            using (SHA256 sha = SHA256.Create())\n", 1),
    # System.Data.EntityState is in .NET Framework's System.Data.Entity.dll (Entity Framework 1-4), absent on .NET;
    # EF6's EntityState is another type (System.Data.Entity.EntityState), which MVC never skipped.
    ("Html/DefaultDisplayTemplates.cs", "                && metadata.ModelType != typeof(EntityState)\n",
     "#if NETFRAMEWORK // webForCore: System.Data.EntityState (System.Data.Entity.dll) does not exist on .NET\n"
     "                && metadata.ModelType != typeof(EntityState)\n#endif\n", 1),
    ("Html/DefaultEditorTemplates.cs", "                && metadata.ModelType != typeof(EntityState)\n",
     "#if NETFRAMEWORK // webForCore: System.Data.EntityState (System.Data.Entity.dll) does not exist on .NET\n"
     "                && metadata.ModelType != typeof(EntityState)\n#endif\n", 1),
    ("ModelBinders.cs",) + USING_LINQ,
    ("ModelBinders.cs", "                { typeof(Binary), new LinqBinaryModelBinder() },\n",
     "#if NETFRAMEWORK\n                { typeof(Binary), new LinqBinaryModelBinder() },\n#endif\n", 1),
]

NO_LINQ = "System.Data.Linq (LINQ to SQL) does not exist on .NET; MVC's Binary support is compiled out."
NO_EF1 = "System.Data.Objects.DataClasses.EntityObject (System.Data.Entity.dll) does not exist on .NET."
MEMBERS_OUT["System.Web.Mvc.Test"] = [
    ("Html/Test/InputExtensionsTest.cs", "public void HiddenWithBinaryArrayValueRendersBase64EncodedValue()", NO_LINQ),
    ("Html/Test/InputExtensionsTest.cs", "public void HiddenForWithBinaryValueRendersBase64EncodedValue()", NO_LINQ),
    ("Html/Test/DefaultDisplayTemplatesTest.cs", "public void ObjectTemplateAllPropertiesFromEntityObjectAreHidden()", NO_EF1),
    ("Html/Test/DefaultDisplayTemplatesTest.cs", "private class MyEntityObject : EntityObject", NO_EF1),
    ("Html/Test/DefaultEditorTemplatesTest.cs", "public void ObjectTemplateAllPropertiesFromEntityObjectAreHidden()", NO_EF1),
    ("Html/Test/DefaultEditorTemplatesTest.cs", "private class MyEntityObject : EntityObject", NO_EF1),
]
USING = lambda ns: ("using " + ns + ";\n", "#if NETFRAMEWORK // webForCore: not on .NET\nusing " + ns + ";\n#endif\n", 1)
# webForCore: the tests that use AppDomainUtils.RunInSeparateAppDomain run unchanged (see System.Web.Http.Test).
MEMBER_EDITS["System.Web.Mvc.Test"] = []
# Newlines (tests only, decided 30 Oct 2026). DefaultEditorTemplates.ObjectTemplate writes "\r\n" (on every platform,
# as on .NET Framework); its tests spell it Environment.NewLine.
MEMBER_EDITS["System.Web.Mvc.Test"] += [
    ("Html/Test/DefaultEditorTemplatesTest.cs", f"public void {m}()", ("replace", "Environment.NewLine", '"\\r\\n"'),
     "ObjectTemplate writes \"\\r\\n\" on every platform, as on .NET Framework.")
    for m in ("ObjectTemplateEditsSimplePropertiesOnObjectByDefault", "ObjectTemplate_AddsHtmlAttributes", "ObjectTemplateWithModelError",
              "ObjectTemplateWithDisplayNameMetadata", "ObjectTemplateWithShowForEditorMetadata",
              "ObjectTemplatePreventsRecursionOnModelValue", "ObjectTemplatePreventsRecursionOnModelTypeForNullModelValues")]
# SelectExtensions and the form's client validation script write the platform newline; these tests spell it CRLF.
MEMBER_EDITS["System.Web.Mvc.Test"] += [
    (f"Html/Test/{f}.cs", f"public void {m}()", "verbatim_newlines", "the helper writes the platform newline (AppendLine).")
    for f, m in [("FormExtensionsTest", "BeginFormWithClientValidationEnabled")] + [("SelectExtensionsTest", m) for m in (
        "DropDownList_WithGroups", "DropDownList_WithGroups_WithDisabled", "DropDownList_SelectList_WithGroups",
        "DropDownListFor_WithGroups", "DropDownListFor_WithGroups_WithDisabled", "DropDownListFor_SelectList_WithGroups",
        "ListBox_WithGroups", "ListBox_WithGroups_WithDisabled", "ListBox_MultiSelectList_WithGroups",
        "ListBoxFor_WithGroups", "ListBoxFor_WithGroups_WithDisabled", "ListBoxFor_MultiSelectList_WithGroups")]]
# Accepted platform differences: messages from the .NET BCL.
RE_TRANSFORMS["System.Web.Mvc.Test"] = [
    ("**/*.cs", r'\\r\\nParameter name: (\w+)"', r" (Parameter '\1')" + '"', 19),
    ("**/*.cs", r'"\s*\+\s*Environment\.NewLine\s*\+\s*"Parameter name: (\w+)"', r" (Parameter '\1')" + '"', 27),
]
# Razor/Test builds parser input with Environment.NewLine but checks spans measured with \r\n (as System.Web.Razor.Test).
TRANSFORMS["System.Web.Mvc.Test"] = [
    ("Razor/Test/*.cs", "Environment.NewLine", '"\\r\\n"', 7),
    # Thread.Abort is not supported on .NET. The fork's Response.End throws a ThreadAbortException instance (HttpResponse),
    # the only way one reaches MVC here, so the tests throw one the same way. ResetAbort is not needed for a thrown one.
    ("**/*.cs", "Thread.CurrentThread.Abort();",
     "throw (ThreadAbortException)Activator.CreateInstance(typeof(ThreadAbortException), true); // webForCore: as the fork's Response.End", 7),
    ("**/*.cs", "Thread.ResetAbort();", "/* webForCore: Thread.ResetAbort() not needed for a thrown ThreadAbortException */", 7),
    # Assembly-qualified type names: mscorlib is System.Private.CoreLib on .NET.
    ("Html/Test/*.cs", "mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", '" + typeof(int).Assembly.FullName + "', 10),
]
# AssemblyBuilderAccess.Save is .NET Framework only; .NET 9+ has PersistedAssemblyBuilder for assemblies saved to disk.
PERSISTED = "// webForCore: AssemblyBuilderAccess.Save is .NET Framework only; PersistedAssemblyBuilder saves on .NET.\n"
FIXUPS["System.Web.Mvc.Test"] = [
    ("Test/MvcTestHelper.cs", "            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(\n",
     "            " + PERSISTED + "            PersistedAssemblyBuilder assemblyBuilder = new PersistedAssemblyBuilder(\n", 4),
    ("Test/MvcTestHelper.cs", "                AssemblyBuilderAccess.Save);\n", "                typeof(object).Assembly);\n", 4),
] + [("Test/MvcTestHelper.cs", f'                "MvcAssembly{i}", "MvcAssembly{i}.dll");\n', f'                "MvcAssembly{i}");\n', 1)
     for i in range(1, 5)] + [
    ("Html/Test/InputExtensionsTest.cs",) + USING("System.Data.Linq"),
    ("Html/Test/InputExtensionsTest.cs", "            public Binary binary { get; set; }\n",
     "#if NETFRAMEWORK // webForCore: " + NO_LINQ + "\n            public Binary binary { get; set; }\n#endif\n", 1),
    ("Html/Test/DefaultDisplayTemplatesTest.cs",) + USING("System.Data.Objects.DataClasses"),
    ("Html/Test/DefaultEditorTemplatesTest.cs",) + USING("System.Data.Linq"),
    ("Html/Test/DefaultEditorTemplatesTest.cs",) + USING("System.Data.Objects.DataClasses"),
    ("Html/Test/DefaultEditorTemplatesTest.cs",
     "            Assert.Equal(\n"
     "                \"&quot;AQIDBAU=&quot;<input id=\\\"FieldPrefix\\\" name=\\\"FieldPrefix\\\" type=\\\"hidden\\\" value=\\\"AQIDBAU=\\\" />\",\n"
     "                DefaultEditorTemplates.HiddenInputTemplate(MakeHtmlHelper<Binary>(new Binary(byteValues))));\n",
     "#if NETFRAMEWORK // webForCore: " + NO_LINQ + "\n"
     "            Assert.Equal(\n"
     "                \"&quot;AQIDBAU=&quot;<input id=\\\"FieldPrefix\\\" name=\\\"FieldPrefix\\\" type=\\\"hidden\\\" value=\\\"AQIDBAU=\\\" />\",\n"
     "                DefaultEditorTemplates.HiddenInputTemplate(MakeHtmlHelper<Binary>(new Binary(byteValues))));\n#endif\n", 1),
    ("Test/ModelBindersTest.cs",) + USING("System.Data.Linq"),
    # Accepted platform differences: .NET BCL messages.
    ("Test/RemoteAttributeTest.cs", '"The given key was not present in the dictionary.");',
     '"The given key \'type\' was not present in the dictionary."); // webForCore: .NET names the key', 1),
    ("Util/DictionaryHelper.cs", '"An item with the same key has already been added."\n',
     '"An item with the same key has already been added. Key: " + pairs[1].Key // webForCore: .NET names the key\n', 1),
    ("Html/Test/InputExtensionsTest.cs", '"String was not recognized as a valid Boolean.");',
     '"String \'NotTrue\' was not recognized as a valid Boolean."); // webForCore: .NET quotes the input', 2),
    # .NET's Activator message already names the type, so MVC's EnsureDebuggableException keeps the original exception.
    ("Test/BuildManagerCompiledViewTest.cs",
     '"No parameterless constructor defined for this object. Object type \'System.Web.Mvc.Test.CompiledTypeViewTest+NoParameterlessCtor\'.");',
     '"Cannot dynamically create an instance of type \'System.Web.Mvc.Test.CompiledTypeViewTest+NoParameterlessCtor\'. Reason: No parameterless constructor defined."); // webForCore: .NET\'s message', 1),
    ("Test/BuildManagerViewEngineTest.cs",
     '"No parameterless constructor defined for this object. Object type \'System.Web.Mvc.Test.BuildManagerViewEngineTest+NoParameterlessCtor\'.");\n'
     '            Assert.Contains("System.Activator.CreateInstance(", ex.InnerException.StackTrace);',
     '"Cannot dynamically create an instance of type \'System.Web.Mvc.Test.BuildManagerViewEngineTest+NoParameterlessCtor\'. Reason: No parameterless constructor defined.");\n'
     '            // webForCore: .NET\'s message names the type, so MVC keeps the original exception (no InnerException).\n'
     '            Assert.Contains("System.RuntimeType.ActivatorCache", (ex.InnerException ?? ex).StackTrace); // webForCore: .NET\'s frames', 1),
    # .NET's stack shows RuntimeType.ActivatorCache where .NET Framework's showed System.Activator.CreateInstance.
    ("Test/BuildManagerCompiledViewTest.cs", 'Assert.Contains("System.Activator.CreateInstance(", ex.ToString());',
     'Assert.Contains("System.RuntimeType.ActivatorCache", ex.ToString()); // webForCore: .NET\'s frames', 1),
    ("Test/DefaultModelBinderTest.cs", 'Assert.Contains("System.Activator.CreateInstance(", exception.ToString());',
     'Assert.Contains("System.RuntimeType.CreateInstanceDefaultCtor", exception.ToString()); // webForCore: .NET\'s frames', 1),
    # A ThreadAbortException thrown as the fork's Response.End does carries the default message, not Thread.Abort's.
    ("Test/ControllerActionInvokerTest.cs", '"Thread was being aborted."',
     '"System error." /* webForCore: a thrown ThreadAbortException\'s default message */', 2),
    ("Test/DefaultModelBinderTest.cs",
     '"No parameterless constructor defined for this object. Object type \'System.Web.Mvc.Test.DefaultModelBinderTest+NoParameterlessCtor\'.");',
     '"No parameterless constructor defined for type \'System.Web.Mvc.Test.DefaultModelBinderTest+NoParameterlessCtor\'."); // webForCore: .NET\'s message', 1),
    # .NET's Attribute.GetCustomAttributes casts a non-runtime ParameterInfo's result to Attribute[]; .NET Framework used "as".
    ("Test/ReflectedParameterDescriptorTest.cs",
     "            return new ReflectedParameterDescriptor(parameterInfo, new Mock<ActionDescriptor>().Object);\n",
     "            // webForCore: .NET's Attribute.GetCustomAttribute casts a mock ParameterInfo's object[] to Attribute[]\n"
     "            // (.NET Framework used \"as\" and got null), so the mock answers the BindAttribute lookup (the only one the\n"
     "            // constructor makes) with an empty Attribute[]; the tests' own setups are for other attribute types.\n"
     "            if (parameterInfo is IMocked<ParameterInfo> mocked)\n"
     "            {\n"
     "                mocked.Mock.Setup(pi => pi.GetCustomAttributes(typeof(BindAttribute), It.IsAny<bool>())).Returns(new Attribute[0]);\n"
     "            }\n"
     "            return new ReflectedParameterDescriptor(parameterInfo, new Mock<ActionDescriptor>().Object);\n", 1),
    # ICU's es-MX time format ("4:12:08 p.m.") differs from Windows NLS ("04:12:08 p. m."): build the expectation
    # from the culture, as the template does.
    ("Html/Test/TemplateHelpersTest.cs",
     '                Assert.Equal("Model = 18/11/2009 04:12:08 p. m., ModelType = System.DateTime, RealModelType = System.DateTime, PropertyName = MyProperty, FormattedModelValue = miércoles, 18 de noviembre de 2009 04:12:08 p. m., HtmlFieldPrefix = FieldPrefix.htmlFieldName, TemplateName = templateName, Mode = ReadOnly", result);',
     '                // webForCore: ICU\'s es-MX format ("4:12:08 p.m.") differs from Windows NLS ("04:12:08 p. m."); use the culture\'s.\n'
     '                DateTime value = new DateTime(2009, 11, 18, 16, 12, 8, DateTimeKind.Utc);\n'
     '                Assert.Equal("Model = " + value.ToString(Thread.CurrentThread.CurrentCulture) + ", ModelType = System.DateTime, RealModelType = System.DateTime, PropertyName = MyProperty, FormattedModelValue = " + String.Format(Thread.CurrentThread.CurrentCulture, "{0:F}", value) + ", HtmlFieldPrefix = FieldPrefix.htmlFieldName, TemplateName = templateName, Mode = ReadOnly", result);', 1),
    # BinaryFormatter is removed from .NET; the fork's System.Web serializes with its port, so the tests use it too.
    ("Async/Test/SynchronousOperationExceptionTest.cs", "using System.Runtime.Serialization.Formatters.Binary;\n",
     "using WebFormsForCore.Serialization.Formatters.Binary; // webForCore: the fork's BinaryFormatter port\n", 1),
    ("Test/ModelStateDictionaryTest.cs", "using System.Runtime.Serialization.Formatters.Binary;\n",
     "using WebFormsForCore.Serialization.Formatters.Binary; // webForCore: the fork's BinaryFormatter port\n", 1),
    ("Test/ModelBindersTest.cs",
     "            Assert.Equal(4, binders.Count);\n",
     "            // webForCore: no LinqBinaryModelBinder (" + NO_LINQ + ")\n            Assert.Equal(3, binders.Count);\n", 1),
    ("Test/ModelBindersTest.cs",
     "            Assert.True(binders.ContainsKey(typeof(Binary)));\n"
     "            Assert.IsType<LinqBinaryModelBinder>(binders[typeof(Binary)]);\n", "", 1),
]

NO_CAS = "Code Access Security (legacy CAS policy, AppDomainSetup compatibility switches) does not exist on .NET."
MEMBERS_OUT["System.Web.Helpers.Test"] = [
    ("ServerInfoTest.cs", "public void ServerInfoDoesNotProduceLegacyCasForHomogenousAppDomain()", NO_CAS),
    ("ServerInfoTest.cs", "public void ServerInfoProducesLegacyCasForNonHomogenousAppDomain()", NO_CAS),
    ("ServerInfoTest.cs", "private AppDomainSetup GetAppDomainSetup(bool legacyCasEnabled)", NO_CAS),
]
MEMBERS_OUT["System.Web.WebPages.Test"] = [
    ("Html/InputHelperTest.cs", "public void HiddenWithBinaryArrayValueRendersBase64EncodedValue()", NO_LINQ),
]
FIXUPS["System.Web.WebPages.Test"] = [("Html/InputHelperTest.cs",) + USING("System.Data.Linq")]

NO_SXS = ("side-by-side Web Pages versions (another System.Web.WebPages.Deployment loaded next to this one, as from the GAC): .NET has no "
          "GAC and an AssemblyLoadContext holds one version of a name.")
# WebImage and Chart run on System.Drawing.Common with GDI+ on SkiaSharp (src/webForCore.System.Drawing.Common,
# tools/lift/lift_drawing.py). libgdiplus was tried first and rejected (LIFT.md).
TRANSFORMS["System.Web.Helpers.Test"] = []
# Windows paths in the test data (tests only): a drive-rooted path is rooted on Windows, so WebImage takes it as a file
# path; on Linux it is not. "x:\dir\file.jpg" becomes "/x/dir/file.jpg", rooted on both. ChartTest strips the drive
# letter from the current directory; it strips one only if there is one.
RE_TRANSFORMS["System.Web.Helpers.Test"] = [
    ("WebImageTest.cs", r'@"([a-zA-Z]):\\([^"]*)"', lambda m: '"/' + m.group(1) + '/' + m.group(2).replace("\\", "/") + '"', 24),
    ("ChartTest.cs", re.escape("Directory.GetCurrentDirectory().Substring(2)"),
     lambda m: 'System.Text.RegularExpressions.Regex.Replace(Directory.GetCurrentDirectory(), "^[A-Za-z]:", "") /* webForCore: no drive letter on Linux */', 1),
]
MEMBER_EDITS["System.Web.Helpers.Test"] = [
    ("ObjectInfoTest.cs", "public void PrintDateTime()", ("replace", '"11/20/2001 10:30:01 AM"', "dt.ToString()"),
     "ICU's en-US time format puts U+202F before AM; the culture's own format."),
    ("ObjectInfoTest.cs", "public void PrintCustomObjectPrintsMembers()",
     ("replace", '"DateTime Dob = 11/19/1986 12:00:00 AM"', '"DateTime Dob = " + new DateTime(1986, 11, 19)'),
     "ICU's en-US time format puts U+202F before AM; the culture's own format."),
    ("ServerInfoTest.cs", "public void EnvironmentVariablesReturnsExpectedInfo()",
     ("replace", '            VerifyKey(envVariables, "Path");\n            VerifyKey(envVariables, "SystemDrive");\n',
      '            VerifyKey(envVariables, "PATH");\n'),
     "Linux has PATH (environment variable names are case sensitive) and no SystemDrive."),
]
FIXUPS["System.Web.Helpers"] = [
    ("WebGrid/WebGridDataSource.cs", "            catch (ArgumentException)\n",
     "            // webForCore: .NET's sort wraps the comparer's ArgumentException in an InvalidOperationException (\"Failed to\n"
     "            // compare two elements in the array\"); .NET Framework's LINQ sort let it through.\n"
     "            catch (Exception e) when (e is ArgumentException || (e is InvalidOperationException && e.InnerException is ArgumentException))\n", 1),
]
# Microsoft's suite races: the ScopeStorage tests reset the static ScopeStorage while xunit runs them in parallel with
# the classes Microsoft put in the "Uses ScopeStorage or ViewEngines.Engines" collection (Html/ValidationHelperTest failed
# one run in two). They join that collection.
RE_TRANSFORMS["System.Web.WebPages.Test"] = [
    ("ScopeStorage/*.cs", r"(?m)^(    public (?:sealed )?class \w+)",
     '    [Xunit.Collection("Uses ScopeStorage or ViewEngines.Engines")] // webForCore: resets the static ScopeStorage\r\n\\1', 4)]
MEMBER_EDITS["System.Web.WebPages.Test"] = [
    ("WebPage/LayoutTest.cs", "public void LayoutSectionsTest()", ("replace", "Environment.NewLine", '"\\r\\n"'),
     "the layout writes \"\\r\\n\" (Razor's literal text from a CRLF source), as on .NET Framework."),
    ("WebPage/BuildManagerExceptionUtilTest.cs", "public void CodeDomDefinedExtensionThrowsTest()", "skip",
     ".NET Framework's machine.config registers a CodeDom provider for .js (JScript .NET); JScript does not exist on .NET."),
    ("Helpers/Claims/ClaimsIdentityConverterTest.cs", "public void TryConvert_SkipsGrandfatheredTypes(IIdentity identity)", "skip",
     "WindowsIdentity is Windows only (handoff section 10)."),
    ("Extensions/StringExtensionsTest.cs", "public void AsDecimalUsesCurrentCulture()",
     ("replace", 'Assert.Equal("12345.00", value.ToString', 'Assert.Equal("12345" + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + "00", value.ToString'),
     "ICU's ar-DZ decimal separator is \",\" (Windows NLS: \".\"); the culture's own."),
]
FIXUPS["System.Web.WebPages.Test"] += [
    ("Mvc/HttpAntiForgeryExceptionTest.cs", "using System.Runtime.Serialization.Formatters.Binary;\n",
     "using WebFormsForCore.Serialization.Formatters.Binary; // webForCore: the fork's BinaryFormatter port\n", 1),
]
MEMBER_EDITS["System.Web.WebPages.Razor.Test"] = [
    ("WebCodeRazorEngineHostTest.cs", "public void ConstructorWithMalformedVirtualPathSetsDefaultProperties()", "skip",
     "the path uses \\\\ as a separator, which only Windows treats as one."),
    ("RazorBuildProviderTest.cs", "public void CodeCompilerTypeReturnsTypeFromCodeLanguage()",
     ("replace", "Assert.Equal(typeof(CSharpCodeProvider), type.CodeDomProviderType);",
      'Assert.Equal("WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider", type.CodeDomProviderType.FullName);'),
     "the fork compiles with its own Roslyn CodeDom provider (WebFormsForCore.Compilers), configured in its machine.config."),
]
# Windows paths in test input (Path.Combine with backslashes); the deployment code is path-agnostic.
RE_TRANSFORMS["System.Web.WebPages.Deployment.Test"] = [
    ("WebPagesDeploymentTest.cs", r'@"([^"\\]*)\\([^"]*)"', lambda m: '@"' + m.group(1) + "/" + m.group(2).replace("\\", "/") + '"', None),
    ("PreApplicationStartCodeTest.cs", r'@"([^"\\]*)\\([^"]*)"', lambda m: '@"' + m.group(1) + "/" + m.group(2).replace("\\", "/") + '"', None),
]
MEMBER_EDITS["System.Web.WebPages.Deployment.Test"] = [
    ("WebPagesDeploymentTest.cs", f"public void {m}()", "skip", NO_SXS) for m in (
        "GetVersionReturnsLowerVersionIfSpecifiedInConfig", "GetVersionReturnsLowerVersionIfSpecifiedInConfigAndNotExplicitlyDisabled",
        "GetVersionIgnoresUnsignedConfigDll", "GetVersionReturnsV1IfCshtmlFilePresentButNoVersionIsSpecifiedInConfigOrBin")]
FIXUPS["System.Web.WebPages.Deployment.Test"] = [
    # Microsoft.Web.Infrastructure's version comes from the assembly the app runs with: 2.0.0.0 here (the current
    # nuget.org package), 1.0.0.0 in the Web Stack's own build.
    ("AssemblyUtilsTest.cs", '                "Microsoft.Web.Infrastructure, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35",\n',
     '                // webForCore: the Microsoft.Web.Infrastructure this runs with (2.0.0.0, as on nuget.org; the Web Stack built against 1.0.0.0).\n'
     '                typeof(Microsoft.Web.Infrastructure.InfrastructureHelper).Assembly.GetName().ToString(),\n', 1),
    ("AssemblyUtilsTest.cs", 'AssemblyNameFor("Microsoft.Web.Infrastructure", "1.0.0.0"),',
     'typeof(Microsoft.Web.Infrastructure.InfrastructureHelper).Assembly.GetName().ToString(), // webForCore: as above', 1),
]
for _suite in ("System.Web.WebPages.Test", "System.Web.WebPages.Razor.Test", "System.Web.WebPages.Deployment.Test", "System.Web.Helpers.Test"):
    RE_TRANSFORMS.setdefault(_suite, [])
    RE_TRANSFORMS[_suite] += [
        ("**/*.cs", r'\\r\\nParameter name: (\w+)"', r" (Parameter '\1')" + '"', None),
        ("**/*.cs", r'"\s*\+\s*Environment\.NewLine\s*\+\s*"Parameter name: (\w+)"', r" (Parameter '\1')" + '"', None),
    ]

FIXUPS["Microsoft.Web.Mvc"] = [
    ("ModelBinding/BinaryDataModelBinderProvider.cs",) + USING_LINQ,
    ("ModelBinding/BinaryDataModelBinderProvider.cs",
     "            new SimpleModelBinderProvider(typeof(Binary), new LinqBinaryExtensibleModelBinder())\n",
     "#if NETFRAMEWORK // webForCore: " + NO_LINQ + "\n"
     "            new SimpleModelBinderProvider(typeof(Binary), new LinqBinaryExtensibleModelBinder())\n#endif\n", 1),
]
NO_NDCS = ("// webForCore: NetDataContractSerializer is .NET Framework only. Html.Serialize and [Deserialize] fail with a\n"
           "            // SerializationException (CreateSerializationException wraps this), as Microsoft's Futures binary does on .NET.\n")
FIXUPS["Microsoft.Web.Mvc"] += [
    ("MvcSerializer.cs",
     "            using (XmlDictionaryReader dr = XmlDictionaryReader.CreateBinaryReader(rawBytes, XmlDictionaryReaderQuotas.Max))\n"
     "            {\n"
     "                object deserialized = new NetDataContractSerializer().ReadObject(dr);\n"
     "                return deserialized;\n"
     "            }\n",
     "            " + NO_NDCS + "            throw new PlatformNotSupportedException(\"NetDataContractSerializer is not supported on .NET.\");\n", 1),
    ("MvcSerializer.cs",
     "            using (XmlDictionaryWriter dw = XmlDictionaryWriter.CreateBinaryWriter(outputStream, null, null, ownsStream: false))\n"
     "            {\n"
     "                new NetDataContractSerializer().WriteObject(dw, graph);\n"
     "            }\n",
     "            " + NO_NDCS + "            throw new PlatformNotSupportedException(\"NetDataContractSerializer is not supported on .NET.\");\n", 1),
    ("Resources/XmlFormatHandler.cs",
     "            DataContractSerializer xml = new DataContractSerializer(bindingContext.ModelType, null, Int32.MaxValue, true, true, null);\n",
     "            // webForCore: .NET has the same settings as DataContractSerializerSettings (no data contract surrogate here: null).\n"
     "            DataContractSerializer xml = new DataContractSerializer(bindingContext.ModelType, new DataContractSerializerSettings\n"
     "            {\n"
     "                KnownTypes = null, MaxItemsInObjectGraph = Int32.MaxValue, IgnoreExtensionDataObject = true, PreserveObjectReferences = true,\n"
     "            });\n", 1),
]
FIXUPS["Microsoft.Web.Mvc"] += [
    ("FileExtensionsAttribute.cs",
     "            try\n            {\n                return ExtensionsParsed.Contains(Path.GetExtension(fileName).ToLowerInvariant());\n",
     "            // webForCore: .NET Framework's Path.GetExtension threw ArgumentException for these characters (Path.CheckInvalidPathChars),\n"
     "            // so such a name was invalid; .NET's no longer checks.\n"
     "            if (fileName.IndexOfAny(FrameworkInvalidPathChars) >= 0)\n"
     "            {\n"
     "                return false;\n"
     "            }\n"
     "            try\n            {\n                return ExtensionsParsed.Contains(Path.GetExtension(fileName).ToLowerInvariant());\n", 1),
    ("FileExtensionsAttribute.cs",
     "        public override bool IsValid(object value)\n",
     "        // webForCore: .NET Framework's Path.InvalidPathChars.\n"
     "        private static readonly char[] FrameworkInvalidPathChars = new[] { '\"', '<', '>', '|', '\\0' }\n"
     "            .Concat(System.Linq.Enumerable.Range(1, 31).Select(c => (char)c)).ToArray();\n"
     "\n"
     "        public override bool IsValid(object value)\n", 1),
]
FIXUPS["Microsoft.Web.Mvc.Test"] = [("ModelBinding/Test/BinaryDataModelBinderProviderTest.cs",) + USING("System.Data.Linq")]
NO_NDCS_TEST = "MvcSerializer needs NetDataContractSerializer, which .NET does not have."
MEMBER_EDITS["Microsoft.Web.Mvc.Test"] = [
    ("Test/MvcSerializerTest.cs", f"public void {m}()", "skip", NO_NDCS_TEST)
    for m in ("SerializeAllowsNullValues", "SerializeAndDeserializeRoundTripsValue")]
RE_TRANSFORMS["Microsoft.Web.Mvc.Test"] = [
    ("**/*.cs", r'\\r\\nParameter name: (\w+)"', r" (Parameter '\1')" + '"', None),
    ("**/*.cs", r'"\s*\+\s*Environment\.NewLine\s*\+\s*"Parameter name: (\w+)"', r" (Parameter '\1')" + '"', None),
    # .NET's Activator message names the type, so the Futures binders keep the original MissingMethodException.
    ("**/*.cs", r'"No parameterless constructor defined for this object\. Object type \'([^\']+)\'\."',
     r'"Cannot dynamically create an instance of type \'\1\'. Reason: No parameterless constructor defined."', None),
    # .NET's Activator message writes a generic type as Type.ToString() does; the Futures binders' EnsureDebuggableException
    # then appends the full name, which that form does not contain.
    ("ModelBinding/Test/*.cs", re.escape('NoParameterlessCtorBinder`1[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]\\\'. Reason: No parameterless constructor defined.");'), lambda m: 'NoParameterlessCtorBinder`1[System.Int32]\\\'. Reason: No parameterless constructor defined. Object type \\\'" + typeof(NoParameterlessCtorBinder<int>).FullName + "\\\'."); // webForCore: .NET\'s message', 2),
]
TRANSFORMS["Microsoft.Web.Mvc.Test"] = [
    # .NET's frames: RuntimeType.ActivatorCache / CreateInstanceDefaultCtor (no System.Activator.CreateInstance frame).
    ("ModelBinding/Test/*.cs", 'Assert.Contains("System.Activator.CreateInstance(", exception.ToString());',
     'Assert.Contains("System.RuntimeType.", exception.ToString()); // webForCore: .NET\'s Activator frames', 4),
]
MEMBERS_OUT["Microsoft.Web.Mvc.Test"] = [
    ("ModelBinding/Test/BinaryDataModelBinderProviderTest.cs", "public void BindModel_GoodValue_LinqBinary_Succeeds()", NO_LINQ),
]
MEMBERS_OUT["Microsoft.Web.Mvc"] = [
    ("ModelBinding/BinaryDataModelBinderProvider.cs", "private class LinqBinaryExtensibleModelBinder : ByteArrayExtensibleModelBinder", NO_LINQ),
]

# Katana: .NET Framework-only APIs.
LOADER_FIXUP = ("Loader/DefaultLoader.cs",
                "                IEnumerable<string> searchPaths = new string[0];\n"
                "                if (info.PrivateBinPathProbe == null || string.IsNullOrWhiteSpace(info.PrivateBinPath))\n"
                "                {\n"
                "                    // Check the current directory\n"
                "                    searchPaths = searchPaths.Concat(new string[] { string.Empty });\n"
                "                }\n"
                "                if (!string.IsNullOrWhiteSpace(info.PrivateBinPath))\n"
                "                {\n"
                "                    // PrivateBinPath may be a semicolon separated list of subdirectories.\n"
                "                    searchPaths = searchPaths.Concat(info.PrivateBinPath.Split(';'));\n"
                "                }\n",
                "                // webForCore: .NET has no private bin path (AppDomainSetup.PrivateBinPath); an app's assemblies\n"
                "                // are in its ApplicationBase, the directory .NET Framework checks when no private bin path is set.\n"
                "                IEnumerable<string> searchPaths = new string[] { string.Empty };\n", 1)
FIXUPS["Microsoft.Owin.Host.SystemWeb"] = [LOADER_FIXUP]
# Katana enables WebSockets on System.Web when it runs on IIS 8 or later (stage one), then checks each request's
# WEBSOCKET_VERSION server variable (stage two). On Kestrel there is no IIS version; the fork's worker request provides
# HttpContext.AcceptWebSocketRequest and WEBSOCKET_VERSION, so stage one also allows a host without IIS.
FIXUPS["Microsoft.Owin.Host.SystemWeb"].append(
    ("OwinAppContext.cs", "            if (HttpRuntime.IISVersion != null && HttpRuntime.IISVersion.Major >= 8)\n",
     "            if (HttpRuntime.IISVersion == null || HttpRuntime.IISVersion.Major >= 8) // webForCore: or no IIS (Kestrel)\n", 1))

# The middleware tests' servers get TestDataProtectionProvider (tools/lift/additions): DPAPI is Windows only.
DP = ('                app.Properties["host.AppName"] = "Microsoft.Owin.Security.Tests";\n',
      '                app.Properties["host.AppName"] = "Microsoft.Owin.Security.Tests";\n'
      "                // webForCore: TestServer has no data protection and DPAPI is Windows only (TestDataProtectionProvider.cs).\n"
      "                Microsoft.Owin.Security.DataProtection.AppBuilderExtensions.SetDataProtectionProvider(app, new TestDataProtectionProvider());\n", 1)
FIXUPS["Microsoft.Owin.Security.Tests"] = [("Cookies/CookieMiddlewareTests.cs",) + DP, ("OAuth/OAuth2TestServer.cs",) + DP]
FIXUPS["Microsoft.Owin.Hosting"] = [LOADER_FIXUP,
    ("Starter/HostingStarterFactory.cs",
     "                return _hostingStarterActivator.Activate(typeof(DomainHostingStarter));\n",
     "                // webForCore: the Domain starter runs the app in a new AppDomain, which .NET does not support\n"
     "                // (AppDomain.CreateDomain throws PlatformNotSupportedException).\n"
     "                throw new PlatformNotSupportedException(\"The Domain hosting starter needs AppDomains, which .NET does not support.\");\n", 1)]
# IdnMapping on .NET (ICU): GetAscii(string) returns an all-ASCII host unchanged, GetAscii(string, index, count)
# lowercases it. On .NET Framework both preserved case.
FIXUPS["Microsoft.Owin"] = [
    ("HostString.cs", "                return mapping.GetAscii(_value, 0, index) + port;\n",
     "                // webForCore: on .NET, GetAscii(s, index, count) lowercases an ASCII host and GetAscii(s) does not; on\n"
     "                // .NET Framework both kept its case. Map the host part as a string of its own, as the no-port case does.\n"
     "                return mapping.GetAscii(_value.Substring(0, index)) + port;\n", 1),
]
FIXUPS["Microsoft.Owin.Tests"] = [
    ("HostStringTests.cs",
     "            // This is a known limitation of the IdnMapping library.\n"
     "            Assert.Throws<ArgumentException>(() => HostString.FromUriComponent(\"Uni\ub9d0code.xn--unicode-s232a\"));\n",
     "            // This is a known limitation of the IdnMapping library.\n"
     "            // webForCore: .NET's IdnMapping (ICU outside Windows) decodes a host that mixes Unicode and punycode labels;\n"
     "            // .NET Framework's threw ArgumentException.\n"
     "            Assert.Equal(\"uni\ub9d0code.uni\ub9d0code\", HostString.FromUriComponent(\"Uni\ub9d0code.xn--unicode-s232a\").Value);\n", 1),
]
# Katana's HttpListener server reads .NET Framework's private HttpListenerRequest fields unless it runs on Mono.
FIXUPS["Microsoft.Owin.Host.HttpListener"] = [
    ("OwinHttpListener.cs",
     "        private static readonly bool IsMono = Type.GetType(\"Mono.Runtime\") != null;\n",
     "        // webForCore: .NET's HttpListener has none of .NET Framework's private fields (m_CookedUrlPath, ...). Outside\n"
     "        // Windows it is the managed implementation that came from Mono, so take Katana's Mono paths (path and query\n"
     "        // from Request.Url, no http.sys queue length). Without this the field read fails and the request never ends.\n"
     "        private static readonly bool IsMono = Type.GetType(\"Mono.Runtime\") != null\n"
     "            || typeof(HttpListenerRequest).GetField(\"m_CookedUrlPath\", BindingFlags.NonPublic | BindingFlags.Instance) == null;\n", 1),
]
FIXUPS["Microsoft.Owin.Security"] = [
    # CERT_CONTEXT behind X509Certificate.Handle: Windows CryptoAPI only.
    ("CertificateSubjectPublicKeyInfoValidator.cs",
     '            // Get a native cert_context from the managed X590Certificate2 instance.\n'
     '            var certContext = (NativeMethods.CERT_CONTEXT)Marshal.PtrToStructure(certificate.Handle, typeof(NativeMethods.CERT_CONTEXT));\n'
     '\n'
     '            // Pull the CERT_INFO structure from the context.\n'
     '            var certInfo = (NativeMethods.CERT_INFO)Marshal.PtrToStructure(certContext.pCertInfo, typeof(NativeMethods.CERT_INFO));\n'
     '\n'
     '            // And finally grab the key information, public key, algorithm and parameters from it.\n'
     '            NativeMethods.CERT_PUBLIC_KEY_INFO publicKeyInfo = certInfo.SubjectPublicKeyInfo;\n'
     '\n'
     '            // Now start encoding to ASN1.\n'
     '            // First see how large the ASN1 representation is going to be.\n'
     '            UInt32 blobSize = 0;\n'
     '            var structType = new IntPtr(NativeMethods.X509_PUBLIC_KEY_INFO);\n'
     '            if (!NativeMethods.CryptEncodeObject(NativeMethods.X509_ASN_ENCODING, structType, ref publicKeyInfo, null, ref blobSize))\n'
     '            {\n'
     '                int error = Marshal.GetLastWin32Error();\n'
     '                throw new Win32Exception(error);\n'
     '            }\n'
     '\n'
     '            // Allocate enough space.\n'
     '            var blob = new byte[blobSize];\n'
     '\n'
     '            // Finally get the ASN1 representation.\n'
     '            if (!NativeMethods.CryptEncodeObject(NativeMethods.X509_ASN_ENCODING, structType, ref publicKeyInfo, blob, ref blobSize))\n'
     '            {\n'
     '                int error = Marshal.GetLastWin32Error();\n'
     '                throw new Win32Exception(error);\n'
     '            }\n'
     '\n'
     '            return blob;\n',
     "            // webForCore: the original reads the CryptoAPI CERT_CONTEXT behind certificate.Handle and DER-encodes its\n"
     "            // SubjectPublicKeyInfo with CryptEncodeObject(X509_PUBLIC_KEY_INFO). Outside Windows the handle is not a\n"
     "            // CERT_CONTEXT; ExportSubjectPublicKeyInfo returns the same DER encoding.\n"
     "            return certificate.PublicKey.ExportSubjectPublicKeyInfo();\n", 1),
    ("DataProtection/DpapiDataProtector.cs",
     "        private readonly System.Security.Cryptography.DpapiDataProtector _protector;\n"
     "\n"
     "        public DpapiDataProtector(string appName, string[] purposes)\n"
     "        {\n"
     "            _protector = new System.Security.Cryptography.DpapiDataProtector(appName, \"Microsoft.Owin.Security.IDataProtector\", purposes)\n"
     "            {\n"
     "                Scope = DataProtectionScope.CurrentUser\n"
     "            };\n"
     "        }\n"
     "\n"
     "        public byte[] Protect(byte[] userData)\n"
     "        {\n"
     "            return _protector.Protect(userData);\n"
     "        }\n"
     "\n"
     "        public byte[] Unprotect(byte[] protectedData)\n"
     "        {\n"
     "            return _protector.Unprotect(protectedData);\n"
     "        }\n",
     "        // webForCore: System.Security.Cryptography.DpapiDataProtector is .NET Framework only. LocalDataProtection makes the\n"
     "        // same DPAPI call (CurrentUser scope) on Windows and emulates it elsewhere (a per-user key file; see\n"
     "        // WebFormsForCore/Security/LocalDataProtection.cs). The entropy is a SHA-256 of the application name and\n"
     "        // purposes, so payloads are not interchangeable with .NET Framework's. On System.Web the host supplies MachineKey\n"
     "        // data protection instead (OwinAppContext).\n"
     "        private readonly byte[] _entropy;\n"
     "\n"
     "        public DpapiDataProtector(string appName, string[] purposes)\n"
     "        {\n"
     "            string[] parts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(\n"
     "                new[] { appName, \"Microsoft.Owin.Security.IDataProtector\" }, purposes ?? new string[0]));\n"
     "            using (SHA256 sha = SHA256.Create())\n"
     "            {\n"
     "                _entropy = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join(\"\\0\", parts)));\n"
     "            }\n"
     "        }\n"
     "\n"
     "        public byte[] Protect(byte[] userData)\n"
     "        {\n"
     "            return WebFormsForCore.Security.LocalDataProtection.Protect(userData, _entropy, machineScope: false);\n"
     "        }\n"
     "\n"
     "        public byte[] Unprotect(byte[] protectedData)\n"
     "        {\n"
     "            return WebFormsForCore.Security.LocalDataProtection.Unprotect(protectedData, _entropy, machineScope: false);\n"
     "        }\n", 1)]



def _decl(line):
    return re.sub(r"^(?:(?:public|private|internal|protected|static)\s+)+", "", line.strip())


def member_span(lines, signature):
    # Access modifiers are ignored ("void X()" and "public void X()" are the same member).
    starts = [i for i, l in enumerate(lines) if _decl(l).startswith(_decl(signature))]
    if len(starts) != 1:
        return None
    first = start = starts[0]
    while first > 0 and lines[first - 1].strip().startswith("["):
        first -= 1
    depth, opened = 0, False
    for i in range(start, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        opened = opened or "{" in lines[i]
        if opened and depth == 0:
            return first, start, i
    return None


# Edits inside one member: (relative path, signature, kind, reason). kind "skip": [Fact]/[Theory] gets Skip=reason and
# the body goes under #if NETFRAMEWORK; ("replace", old, new): replace text within the member (at least once);
# "verbatim_newlines": multi-line verbatim strings get .Replace("\r\n", Environment.NewLine), for output written with
# the platform newline (AppendLine, Environment.NewLine) that the test spells as CRLF.


def edit_member(text, nl, signature, kind, reason):
    lines = text.split(nl)
    span = member_span(lines, signature)
    if span is None:
        return None
    first, start, end = span
    if kind == "skip":
        attr = next((i for i in range(first, start) if lines[i].strip() in ("[Fact]", "[Theory]")), None)
        body = next(i for i in range(start, end + 1) if lines[i].strip() == "{")
        if attr is None:
            return None
        lines[attr] = lines[attr].replace("]", f'(Skip = "webForCore: {reason}")]')
        lines[body + 1:end] = ["#if NETFRAMEWORK"] + lines[body + 1:end] + ["#endif"]
        return nl.join(lines)
    member = nl.join(lines[first:end + 1])
    if kind == "verbatim_newlines":
        # A const string cannot take .Replace(); the member's const strings become plain locals.
        member = re.sub(r"\bconst string\b", "string", member)
        new, n = re.subn(r'@"(?:[^"]|"")*"', lambda m: m.group(0) + '.Replace("\\r\\n", Environment.NewLine)' if nl in m.group(0) else m.group(0), member)
    else:
        _, old, newtext = kind
        n = member.count(old.replace("\n", nl))
        new = member.replace(old.replace("\n", nl), newtext.replace("\n", nl))
    if new == member:
        return None
    indent = lines[first][:len(lines[first]) - len(lines[first].lstrip())]
    return nl.join(lines[:first] + [indent + "// webForCore: " + reason] + new.split(nl) + lines[end + 1:])


# Regex replacements over files: (glob, pattern, replacement, expected total). Applied after TRANSFORMS.


def wrap_member(text, nl, signature, reason):
    lines = text.split(nl)
    starts = [i for i, l in enumerate(lines) if l.strip().startswith(signature)]
    if len(starts) != 1:
        return None
    first = start = starts[0]
    while first > 0 and lines[first - 1].strip().startswith("["):
        first -= 1
    depth, end, opened = 0, None, False
    for i in range(start, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        opened = opened or "{" in lines[i]
        if opened and depth == 0:
            end = i
            break
    if end is None:
        return None
    indent = lines[first][:len(lines[first]) - len(lines[first].lstrip())]
    lines[first:end + 1] = [indent + "// webForCore: " + reason, "#if NETFRAMEWORK"] + lines[first:end + 1] + ["#endif"]
    return nl.join(lines)


# Bulk replacements, applied before FIXUPS: (glob, old, new, expected total).
TRANSFORMS.update({
    # Parser tests build input with Environment.NewLine but hard-code \r\n spans and positions.
    "System.Web.Razor.Test": [("**/*.cs", "Environment.NewLine", '"\\r\\n"', 569)],
})

SDK_DEFINE = re.compile(r"^(TRACE|DEBUG|RELEASE|NET|NETCOREAPP|NETSTANDARD|NETFRAMEWORK)([0-9_]*(_OR_GREATER)?)?$")
KEY = "35MSSharedLib1024.snk"
# The public key of 35MSSharedLib1024.snk (PublicKeyToken 31bf3856ad364e35).
MS_PUBLIC_KEY = ("0024000004800000940000000602000000240000525341310004000001000100b5fc90e7027f67871e773a8fde8938c81dd402ba65b9201d"
                 "60593e96c492651e889cc13f1415ebb53fac1131ae0bd333c5ee6021672d9718ea31a8aebd0da0072f25d87dba6fc90ffd598ed4da35e44c"
                 "398c454307e8e33b8426143daec9f596836f97c8f74750e5975c64e2189f45def46b2a2b1247adc3652bf5c308055da9")


def resolve_case(path):
    """Microsoft's projects are built on Windows, where a csproj may spell a path in another case than the checkout
    (System.Web.WebPages.Deployment.Test: NoCshtmlConfigV1 vs NoCshtmlConfigv1). Find it; the copy keeps the csproj's
    spelling, from which the resource name is computed as on Windows."""
    if os.path.exists(path):
        return path
    current = os.sep
    for part in path.strip(os.sep).split(os.sep):
        candidate = os.path.join(current, part)
        if not os.path.exists(candidate) and os.path.isdir(current):
            candidate = next((os.path.join(current, e) for e in os.listdir(current) if e.lower() == part.lower()), candidate)
        current = candidate
    return current


def evaluate(csproj, tfm=None):
    cmd = ["dotnet", "msbuild", csproj, "-nologo", "-p:Configuration=Release", "-t:PrepareResourceNames",
           "-getItem:Compile", "-getItem:EmbeddedResource", "-getItem:ProjectReference", "-getItem:PackageReference",
           "-getItem:None", "-getProperty:RootNamespace", "-getProperty:AssemblyName", "-getProperty:DefineConstants",
           "-getProperty:NoWarn", "-getProperty:AllowUnsafeBlocks"]
    if tfm:
        cmd.append(f"-p:TargetFramework={tfm}")
    out = subprocess.run(cmd, capture_output=True, text=True, check=True).stdout
    return json.loads(out[out.index("{"):])


def katana_evaluate(csproj, root, cfg):
    """The item lists MSBuild would give Katana's SDK-style project: default globs plus the explicit items."""
    proj_dir = os.path.dirname(csproj)
    tree = open(csproj, encoding="utf-8-sig").read()
    versions = dict(re.findall(r"<(\w+Version)>([^<]+)</\1>", open(os.path.join(root, "eng", "Versions.props")).read()))
    excluded = set()
    for pattern in cfg.get("exclude", []):
        excluded |= {os.path.normpath(p) for p in glob.glob(os.path.join(proj_dir, pattern), recursive=True)}
        excluded |= {os.path.normpath(p) for p in glob.glob(os.path.join(proj_dir, pattern, "**"), recursive=True)}

    def globbed(ext):
        found = []
        for p in sorted(glob.glob(os.path.join(proj_dir, "**", "*" + ext), recursive=True)):
            parts = os.path.relpath(p, proj_dir).split(os.sep)
            if parts[0] not in ("bin", "obj") and os.path.normpath(p) not in excluded:
                found.append({"FullPath": p})
        return found

    compiles = globbed(".cs")
    for inc, link in re.findall(r'<Compile Include="([^"]+)" Link="([^"]+)"', tree):
        compiles.append({"FullPath": os.path.normpath(os.path.join(proj_dir, inc.replace("\\", "/"))), "Link": link})
    projects, packages = [], []
    for inc in re.findall(r'<ProjectReference Include="([^"]+)"', tree):
        name = os.path.basename(inc.replace("\\", "/"))[:-len(".csproj")]
        if name not in cfg.get("drop_refs", set()):
            projects.append({"FullPath": os.path.normpath(os.path.join(proj_dir, inc.replace("\\", "/")))})
    for inc, ver in re.findall(r'<PackageReference Include="([^"]+)" Version="([^"]+)"', tree):
        packages.append({"Identity": inc, "Version": re.sub(r"\$\((\w+)\)", lambda m: versions[m.group(1)], ver)})
    for inc in re.findall(r'<Reference Include="([^"]+)"', tree):
        mapped = KATANA_REFERENCES.get(inc)
        if isinstance(mapped, tuple):
            packages.append({"Identity": mapped[0], "Version": mapped[1]})
        elif mapped:
            projects.append({"FullPath": mapped})  # resolved against the fork in lift()
        elif inc not in KATANA_BUILTIN:
            sys.exit(f"{csproj}: unmapped reference {inc}")
    if cfg.get("test"):
        packages += [{"Identity": i, "Version": v} for i, v in KATANA_TEST_PACKAGES]
    rootns = re.search(r"<RootNamespace>([^<]+)</RootNamespace>", tree).group(1)
    desc = re.search(r"<description>([^<]+)</description>", tree)
    return {"Properties": {"RootNamespace": rootns, "AssemblyName": os.path.basename(csproj)[:-len(".csproj")],
                           "DefineConstants": "",
                           "AllowUnsafeBlocks": (re.search(r"<AllowUnsafeBlocks>([^<]+)</AllowUnsafeBlocks>", tree) or [None, ""])[1], "Description": desc.group(1) if desc else ""},
            "Items": {"Compile": compiles, "EmbeddedResource": globbed(".resx"), "ProjectReference": projects,
                      "PackageReference": packages,
                      "InternalsVisibleTo": re.findall(r'<InternalsVisibleTo Include="([^"]+)"', tree)}}


def project_dir(fork, name):
    return os.path.join(fork, "test" if PROJECTS.get(name, {}).get("test") else "src", f"webForCore.{name}")


def dest_rel(item, roots, webstack):
    # Files inside the original project keep their original relative path. (The SDK also gives them an automatic
    # Link of just the file name, because the spike project lives elsewhere, so Link must not win here.)
    full = item["FullPath"]
    for r in roots:
        base = os.path.join(webstack, r) + os.sep
        if full.startswith(base):
            return os.path.relpath(full, base).replace("\\", "/")
    if item.get("Link"):
        # Shared files (src/Common, CommonAssemblyInfo.cs) use the Link from Microsoft's csproj.
        # Formatting.ns2_0 builds links as "$(Prefix)\%(RecursiveDir)...", which can start with a separator.
        return item["Link"].replace("\\", "/").lstrip("/")
    return "Common/" + os.path.basename(full)


def designer_base_name(resx_full):
    designer = resx_full[:-len(".resx")] + ".Designer.cs"
    if os.path.exists(designer):
        m = re.search(r'ResourceManager\("([^"]+)"', open(designer, encoding="utf-8-sig").read())
        if m:
            return m.group(1)
    return None


def lift(name, cfg, roots, spike, fork, commits):
    kind, path = cfg["source"].split(":", 1)
    katana = kind == "katana"
    # Spike projects point at Web Stack files, so their items resolve against the Web Stack root.
    webstack = roots["katana" if katana else "webstack"]
    repo = "aspnet/AspNetKatana" if katana else "aspnet/AspNetWebStack"
    commit = commits["katana" if katana else "webstack"]
    if kind == "spike":
        src_csproj = os.path.join(spike, path, path + ".csproj")
    else:
        src_csproj = os.path.join(webstack, path)
    ev = katana_evaluate(src_csproj, webstack, cfg) if katana else evaluate(src_csproj, cfg.get("eval_tfm"))
    props, items = ev["Properties"], ev["Items"]
    dest_dir = project_dir(fork, name)
    if os.path.exists(dest_dir):
        shutil.rmtree(dest_dir)
    os.makedirs(dest_dir)
    manifest, compiles, resources, notes = [], [], [], []

    def copy(item):
        rel = dest_rel(item, cfg["roots"], webstack)
        dst = os.path.join(dest_dir, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        data = open(resolve_case(item["FullPath"]), "rb").read()
        # Katana has no .gitattributes: its files are checked out as committed (LF), so they are copied unchanged.
        if not katana and os.path.splitext(dst)[1].lower() not in BINARY_EXTENSIONS and b"\0" not in data:
            data = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
        open(dst, "wb").write(data)
        shutil.copystat(resolve_case(item["FullPath"]), dst)
        manifest.append((os.path.relpath(item["FullPath"], webstack), os.path.relpath(dst, fork)))
        return rel

    for c in items.get("Compile", []):
        if not katana and any(fnmatch.fnmatch(dest_rel(c, cfg["roots"], webstack), pat) for pat in cfg.get("exclude", [])):
            continue
        compiles.append(copy(c))
    # PrepareResourceNames fills LogicalName for non-resx resources with names computed for the spike layout (wrong:
    # flattened to the file name). Keep a LogicalName only if the source project file declares it; otherwise MSBuild
    # computes it from the original layout restored here, as in Microsoft's build.
    src_text = open(src_csproj, encoding="utf-8-sig").read()
    declared = set(re.findall(r'LogicalName="([^"]+)"', src_text)) | set(re.findall(r"<LogicalName>([^<]+)</LogicalName>", src_text))
    for r in items.get("EmbeddedResource", []):
        rel = copy(r)
        logical = r.get("LogicalName") if r.get("LogicalName") in declared else None
        if not logical and rel.endswith(".resx"):
            literal = designer_base_name(r["FullPath"])
            computed = ".".join([props["RootNamespace"]] + [p for p in os.path.dirname(rel).split("/") if p] +
                                [os.path.basename(rel)[:-len(".resx")]])
            base = literal or computed
            logical = base + ".resources"
            notes.append(f"{rel}: LogicalName {logical} ({'from Designer.cs' if literal else 'RootNamespace + path'}; "
                         f"spike build named it {r.get('ManifestResourceName')})")
        resources.append((rel, logical))
    # Files of the fork's own projects compiled into a lifted one (copied, so the fork's copy is the only source).
    for src_rel, rel in cfg.get("fork_sources", []):
        os.makedirs(os.path.dirname(os.path.join(dest_dir, rel)), exist_ok=True)
        shutil.copy2(os.path.join(fork, src_rel), os.path.join(dest_dir, rel))
        compiles.append(rel)
        notes.append(f"added {rel} (copied from {src_rel})")
    # webForCore's own files for a lifted project: tools/lift/additions/<Name>/ (each says so in its header).
    adir = os.path.join(fork, "tools", "lift", "additions", name)
    for a in sorted(glob.glob(os.path.join(adir, "**", "*.cs"), recursive=True)):
        rel = os.path.relpath(a, adir)
        os.makedirs(os.path.dirname(os.path.join(dest_dir, rel)), exist_ok=True)
        shutil.copy2(a, os.path.join(dest_dir, rel))
        compiles.append(rel)
        notes.append(f"added {rel} (tools/lift/additions)")
    if cfg.get("app_config"):
        copy({"FullPath": os.path.join(os.path.dirname(src_csproj) if kind != "spike" else os.path.join(webstack, cfg["roots"][0]), "app.config")})
    for f in cfg.get("compile_extra", []):
        compiles.append(copy({"FullPath": os.path.join(os.path.dirname(src_csproj), f)}))
    for f in cfg.get("files", []):
        copy({"FullPath": os.path.join(os.path.dirname(src_csproj), f)})
    copied = []
    for n in items.get("None", []):
        if n.get("CopyToOutputDirectory"):
            copied.append((copy(n), n["CopyToOutputDirectory"]))

    for pattern, old, new, expected in TRANSFORMS.get(name, []):
        total = 0
        for path in sorted(glob.glob(os.path.join(dest_dir, pattern), recursive=True)):
            raw = open(path, "rb").read()
            text = raw.decode("utf-8-sig")
            if old in text:
                total += text.count(old)
                open(path, "w", encoding="utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8", newline="").write(text.replace(old, new))
        if total != expected:
            sys.exit(f"{name}: transform {old!r} in {pattern} matched {total} times, expected {expected}")
        notes.append(f"transform {pattern}: {old} -> {new} ({total})")

    for rel, old, new, expected in FIXUPS.get(name, []):
        path = os.path.join(dest_dir, rel)
        text = open(path, "rb").read().decode("utf-8-sig")
        nl = "\n" if katana else "\r\n"
        old_crlf, new_crlf = old.replace("\n", nl), new.replace("\n", nl)
        found = text.count(old_crlf)
        if found != expected:
            sys.exit(f"{name}: fixup for {rel} matched {found} times, expected {expected}")
        bom = open(path, "rb").read(3) == b"\xef\xbb\xbf"
        open(path, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(text.replace(old_crlf, new_crlf))
        notes.append(f"fixup {rel}: {found} edit(s)")

    for pattern, regex, repl, expected in RE_TRANSFORMS.get(name, []):
        total = 0
        for path in sorted(glob.glob(os.path.join(dest_dir, pattern), recursive=True)):
            raw = open(path, "rb").read()
            text = raw.decode("utf-8-sig")
            new, n = re.subn(regex, repl, text)
            if n:
                total += n
                open(path, "w", encoding="utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8", newline="").write(new)
        if expected is not None and total != expected:
            sys.exit(f"{name}: regex transform {regex!r} in {pattern} matched {total} times, expected {expected}")
        notes.append(f"regex transform {pattern}: {regex} ({total})")

    for rel, signature, kind, reason in MEMBER_EDITS.get(name, []):
        path = os.path.join(dest_dir, rel)
        raw = open(path, "rb").read()
        edited = edit_member(raw.decode("utf-8-sig"), "\n" if katana else "\r\n", signature, kind, reason)
        if edited is None:
            sys.exit(f"{name}: member edit {kind!r} of {signature!r} in {rel} did not apply")
        open(path, "w", encoding="utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8", newline="").write(edited)
        notes.append(f"member edit {rel}: {signature} ({kind if isinstance(kind, str) else kind[0]})")

    for rel, signature, reason in MEMBERS_OUT.get(name, []):
        path = os.path.join(dest_dir, rel)
        raw = open(path, "rb").read()
        text = raw.decode("utf-8-sig")
        wrapped = wrap_member(text, "\n" if katana else "\r\n", signature, reason)
        if wrapped is None:
            sys.exit(f"{name}: member {signature!r} not found exactly once in {rel}")
        open(path, "w", encoding="utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8", newline="").write(wrapped)
        notes.append(f"compiled out {rel}: {signature}")

    refs = []
    for pr in items.get("ProjectReference", []):
        full = pr["FullPath"] if os.path.isabs(pr["FullPath"]) else os.path.join(fork, pr["FullPath"])
        if full.startswith(os.path.join(fork, "src") + os.sep):
            refs.append(os.path.relpath(full, dest_dir))
        else:
            sib = os.path.basename(full)[:-len(".csproj")].replace(".ns2_0", "")
            if sib in cfg.get("drop_refs", set()):
                continue
            target = os.path.join(project_dir(fork, sib), f"webForCore.{sib}.csproj")
            refs.append(os.path.relpath(target, dest_dir))
    refs += [os.path.relpath(os.path.join(fork, r), dest_dir) for r in cfg.get("fork_refs", [])]
    aliased = [(os.path.relpath(os.path.join(project_dir(fork, sib), f"webForCore.{sib}.csproj"), dest_dir), alias)
               for sib, alias in cfg.get("alias_refs", {}).items()]
    for sib in cfg.get("extra_refs", []):
        refs.append(os.path.relpath(os.path.join(project_dir(fork, sib), f"webForCore.{sib}.csproj"), dest_dir))
    for p in items.get("PackageReference", []):
        if p["Identity"] in PACKAGE_TO_PROJECT:
            sib = PACKAGE_TO_PROJECT[p["Identity"]]
            refs.append(os.path.relpath(os.path.join(project_dir(fork, sib), f"webForCore.{sib}.csproj"), dest_dir))
    pkgs = [(p["Identity"], PACKAGE_VERSION_OVERRIDES.get(p["Identity"], p["Version"]))
            for p in items.get("PackageReference", [])
            if p.get("Version") and p["Identity"] not in DROP_PACKAGES and p["Identity"] not in PACKAGE_TO_PROJECT]
    pkgs += list(cfg.get("packages", []))
    if cfg.get("test") and cfg.get("runner", True):
        pkgs += [(i, v) for i, v in PACKAGE_VERSION_OVERRIDES.items() if i not in {p for p, _ in pkgs}]
    defines = [d for d in props["DefineConstants"].split(";") if d and not SDK_DEFINE.match(d)]
    nowarn = "$(NoWarn);CS1591;CS0618;CS0612;SYSLIB0003;SYSLIB0011;SYSLIB0051"
    shared_targets = os.path.relpath(os.path.join(fork, "src", "webForCore.WebStack.targets"), dest_dir)

    x = ['<Project Sdk="Microsoft.NET.Sdk">', "",
         f"  <!-- Lifted from {repo} {commit} ({cfg['source'].split(':', 1)[1]}) by tools/lift/lift_project.py.",
         "       Apache License 2.0; see LICENSE-APACHE-2.0.txt and NOTICE at the repo root. -->",
         "  <PropertyGroup>",
         f"    <TargetFramework>{cfg['tfm']}</TargetFramework>",
         f"    <AssemblyName>{props['AssemblyName']}</AssemblyName>",
         f"    <RootNamespace>{props['RootNamespace']}</RootNamespace>",
         f"    <PackageId>webForCore.{name[len('System.'):] if name.startswith('System.') else name}</PackageId>",
         "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
         "    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>",
         f"    <GenerateAssemblyInfo>{'true' if katana else 'false'}</GenerateAssemblyInfo>",
         "    <ImplicitUsings>disable</ImplicitUsings>",
         "    <Nullable>disable</Nullable>",
         "    <!-- Microsoft's builds use C# 9 (Web Stack, tools/WebStack.settings.targets) and Latest (Katana). -->",
         f"    <LangVersion>{'latest' if katana else '9.0'}</LangVersion>",
         "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
         f"    <NoWarn>{nowarn}</NoWarn>",
         f"    <DefineConstants>$(DefineConstants);{';'.join(defines)}</DefineConstants>",
         ] + (["    <!-- Not signed, as in Microsoft's build. -->", "    <SignAssembly>false</SignAssembly>"] if cfg.get("unsigned") else [
         "    <!-- Delay signed with Microsoft's public key, as Microsoft's build does: PublicKeyToken=31bf3856ad364e35. -->",
         "    <SignAssembly>true</SignAssembly>",
         "    <DelaySign>true</DelaySign>",
         f"    <AssemblyOriginatorKeyFile>{os.path.relpath(os.path.join(fork, 'src', 'SigningKey', KEY), dest_dir)}</AssemblyOriginatorKeyFile>"])
    for prop, value in cfg.get("properties", {}).items():
        x.append(f"    <{prop}>{value}</{prop}>")
    if katana:
        # The attributes Arcade generates for Microsoft's build (see Microsoft.Owin 4.2.3 on nuget.org).
        x += [f"    <AssemblyVersion>{KATANA_VERSION}</AssemblyVersion>",
              f"    <FileVersion>{KATANA_FILE_VERSION}</FileVersion>",
              f"    <InformationalVersion>{KATANA_VERSION[:-2]}</InformationalVersion>",
              "    <Company>Microsoft Corporation</Company>",
              "    <Copyright>\u00a9 Microsoft Corporation. All rights reserved.</Copyright>",
              f"    <Product>{props['AssemblyName']}</Product>",
              f"    <Description>{props['Description']}</Description>" if props.get("Description") else None,
              "    <NeutralLanguage>en-US</NeutralLanguage>",
              "    <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>"]
        x = [l for l in x if l]
    if props.get("AllowUnsafeBlocks", "").lower() == "true":
        x.append("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>")
    if cfg.get("test"):
        x += ["    <IsPackable>false</IsPackable>"]
        if cfg.get("runner", True):
            x += ["    <IsTestProject>true</IsTestProject>"]
    x += ["  </PropertyGroup>", "", f'  <Import Project="{shared_targets}" />', "", "  <ItemGroup>"]
    x += [f'    <Compile Include="{c}" />' for c in sorted(compiles)]
    x += ["  </ItemGroup>", "", "  <ItemGroup>"]
    # Arcade adds Microsoft's public key to InternalsVisibleTo; the lifted tests are delay signed with it too.
    x += [f'    <InternalsVisibleTo Include="{i}" Key="{MS_PUBLIC_KEY}" />' for i in items.get("InternalsVisibleTo", [])]
    for rel, logical in sorted(resources):
        x.append(f'    <EmbeddedResource Include="{rel}"' + (f' LogicalName="{logical}"' if logical else "") + " />")
    for rel, mode in sorted(copied):
        x.append(f'    <None Include="{rel}" CopyToOutputDirectory="{mode}" />')
    if cfg.get("app_config"):
        # The test's app.config: ConfigurationManager.OpenExeConfiguration reads the entry assembly's config, which is
        # testhost's under dotnet test (on .NET Framework, the test runner used the test assembly's).
        x.append('    <None Include="app.config" Link="testhost.dll.config" CopyToOutputDirectory="PreserveNewest" />')
    x.append("  </ItemGroup>")
    if refs or pkgs or aliased:
        x += ["", "  <ItemGroup>"]
        x += [f'    <ProjectReference Include="{r}" />' for r in refs]
        x += [f'    <ProjectReference Include="{r}" Aliases="{a}" />' for r, a in aliased]
        x += [f'    <PackageReference Include="{i}" Version="{v}" />' for i, v in pkgs]
        x.append("  </ItemGroup>")
    x += ["</Project>", ""]
    with open(os.path.join(dest_dir, f"webForCore.{name}.csproj"), "w", encoding="utf-8") as f:
        f.write("\n".join(x))

    mdir = os.path.join(fork, "tools", "lift", "manifests")
    os.makedirs(mdir, exist_ok=True)
    with open(os.path.join(mdir, f"{name}.tsv"), "w", encoding="utf-8") as f:
        f.write(f"# {repo} {commit}: source path (relative to repo root)\tdestination (relative to fork root)\n")
        f.writelines(f"{a}\t{b}\n" for a, b in sorted(manifest))
    print(f"{name}: {len(compiles)} Compile, {len(resources)} EmbeddedResource, {len(refs)} ProjectReference, "
          f"{len(pkgs)} PackageReference, defines={defines}")
    for n in notes:
        print(f"   {n}")


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    webstack, spike, fork = (os.path.abspath(a) for a in sys.argv[1:4])
    # Katana is expected beside the Web Stack checkout.
    roots = {"webstack": webstack, "katana": os.path.join(os.path.dirname(webstack), "AspNetKatana")}
    commits = {k: subprocess.run(["git", "-C", r, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()
               for k, r in roots.items()}
    names = sys.argv[4:] or list(PROJECTS)
    key_dst = os.path.join(fork, "src", "SigningKey", KEY)
    if not os.path.exists(key_dst):
        shutil.copy2(os.path.join(webstack, "tools", KEY), key_dst)
    for n in names:
        lift(n, PROJECTS[n], roots, spike, fork, commits)


if __name__ == "__main__":
    main()
