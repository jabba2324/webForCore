# Licensing: what's under which licence, and commercial use

**Short answer.** All of webForCore's code is under three permissive open-source licences: MIT, Apache 2.0 and
BSD 3-Clause. All three allow commercial use: you may use, modify and ship the code in commercial, closed-source
products, for free, as long as you keep the copyright and licence notices. The material WebFormsForCore (the project
webForCore forked) had copied or decompiled from closed Microsoft products has been replaced or removed; see
[What was replaced](#what-was-replaced).

This page summarises; [NOTICE](../NOTICE) is the authoritative list of components and copyright holders. It isn't
legal advice: for a product decision, have counsel review NOTICE and this page.

## Which parts have which licence

| Part | Where | Licence | Commercial use |
| --- | --- | --- | --- |
| webForCore's own code (hosting layer, IIS modules, PseudoAppDomain, DPAPI emulation, System.Drawing engine, Microsoft.Web.Infrastructure, generated resource accessors, tools, docs, tests) | throughout; files marked `webForCore` | MIT | ✅ Yes |
| WebFormsForCore's port of System.Web, System.Configuration, System.Web.ApplicationServices, System.Web.Extensions, System.Web.Services, Mobile, DataVisualization and aspnet_compiler, from Microsoft's .NET Framework Reference Source | `src/WebFormsForCore.*` (originals copied by the lift scripts are under `ReferenceSource/` folders) | MIT (Microsoft's Reference Source; port © Simon Jakob Egli) | ✅ Yes |
| ASP.NET MVC 5, Web API 2, Web Pages, Razor, Helpers, CORS, MVC Futures | `src/webForCore.System.Web.*`, `src/webForCore.System.Net.Http.Formatting`, `src/webForCore.Microsoft.Web.Mvc` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| Katana (Microsoft.Owin and friends) | `src/webForCore.Microsoft.Owin*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| SignalR 2 | `src/webForCore.Microsoft.AspNet.SignalR.*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| ASP.NET Identity 2 | `src/webForCore.Microsoft.AspNet.Identity.*` | MIT (© Microsoft) | ✅ Yes |
| System.Drawing.Common 6.0 | `src/webForCore.System.Drawing.Common` | MIT (© .NET Foundation) | ✅ Yes |
| ResX reader and writer (from dotnet/winforms) | `src/WebFormsForCore.Web/WebFormsForCore/System.Resources/winforms` | MIT (© .NET Foundation) | ✅ Yes |
| StronglyTypedResourceBuilder (from Mono); Mono code in System.Web, WebGrease and the SoapFormatter | `src/WebFormsForCore.Web/WebFormsForCore/mono` and others | MIT (© Novell and others) | ✅ Yes |
| BinaryFormatter port, FontConverter (from .NET) | `src/WebFormsForCore.Serialization.Formatters`, `src/WebFormsForCore.Drawing` | MIT (© .NET Foundation) | ✅ Yes |
| ASP.NET Web Optimization, WebGrease, Microsoft Ajax Minifier | `src/WebFormsForCore.Web.Optimization*`, `src/WebFormsForCore.WebGrease` | Apache 2.0 (© Microsoft) | ✅ Yes |
| Redesigner (build-time designer file generator) | `src/WebFormsForCore.Build/Redesigner` | BSD 3-Clause (© Sean Werkema) | ✅ Yes |
| Roslyn compilers (binaries) | `src/WebFormsForCore.Compilers` | MIT (© .NET Foundation) | ✅ Yes |
| Dependencies from NuGet: SkiaSharp, EntityFramework 6, Newtonsoft.Json, CoreWCF, System.Data.SqlClient, ... | restored at build time | MIT or Apache 2.0, each package's own | ✅ Yes |
| Tests lifted from Microsoft and Mono | `test/webForCore.*` | Apache 2.0 or MIT | ✅ Yes (not shipped in packages) |

### What the permissive licences ask of you

| Licence | When you distribute the code or binaries (including inside a commercial product) |
| --- | --- |
| MIT | Include the copyright notice and the licence text |
| BSD 3-Clause | The same as MIT, and don't use the authors' names to endorse your product |
| Apache 2.0 | Include the licence text and the `NOTICE` file; mark files you changed. It also grants a patent licence, which ends if you sue a contributor over patents in the work |

Shipping webForCore's `LICENSE`, `LICENSE-APACHE-2.0.txt`, `LICENSE-BSD-3-CLAUSE.txt` and `NOTICE` with your product
satisfies all three. None of the three requires you to release your own source code, and none charges a fee.
**Trademarks** aren't licensed: don't present your product as Microsoft's or as "ASP.NET", even though the
assemblies keep Microsoft's names for compatibility.

The test suites need two things that aren't in the repository: the System.Drawing test fonts, which
`tools/ci/fetch-test-fonts.sh` downloads (their licence text forbids redistribution), and SQL Server for the Identity
tests, which runs as a container (Developer edition, not shipped).

## What was replaced

WebFormsForCore's fork base contained material that no open licence covered: binaries from .NET Framework, and code
decompiled from .NET Framework assemblies and other Microsoft products. webForCore replaced every piece. Where
Microsoft published the original source under MIT, the decompiled copy was replaced with that original, through a lift
script that records each change. Where no MIT source exists, webForCore wrote the code from the documented public API.

| Was | Now | How |
| --- | --- | --- |
| `webengine.dll`, `webengine4.dll` (.NET Framework native binaries) | Removed | Hashing uses managed code on every OS |
| System.Configuration: ~150 files decompiled from System.Configuration.dll and System.dll | The Reference Source originals (89 files) | `tools/lift/lift_configuration.py` |
| Configuration builders, `WindowsAuthenticationElement` | Written from the documented API | No MIT source exists |
| System.Web.ApplicationServices (decompiled) | The Reference Source originals (21 files) | `tools/lift/lift_appservices.py` |
| aspnet_compiler's front end (decompiled aspnet_compiler.exe) | The Reference Source's `aspnet_compiler.cs` and resources, plus WebFormsForCore's own additions | `tools/lift/lift_aspnet_compiler.py` |
| ResX classes (decompiled System.Windows.Forms.dll) | dotnet/winforms' originals (MIT) | `tools/lift/lift_resx.py` |
| StronglyTypedResourceBuilder (System.Design API reconstruction) | Mono's implementation (MIT) | `tools/lift/lift_strb.py` |
| `BindableTypeAttribute` | The Reference Source original | `tools/lift/lift_strb.py` |
| Microsoft.Web.Infrastructure (decompiled from the EULA-licensed 2.0.0 package) | Written from the documented API | Four files |
| `DataProtector` (decompiled from System.Security.dll), `CallContext`, `AppDomainManager`, `UrlBuilder`, `TransactionOption` | Written from the documented API | |
| Resource accessor classes (`SR`, `Res`, decompiled from build-generated code) | Generated from the projects' own `.resx` files | `tools/lift/gen_sr.py` |
| Assembly identity constants (`AssemblyRef`, `ThisAssembly`, decompiled) | Rewritten with the identity strings they hold | |
| System.Web.RegularExpressions classes (decompiled; never compiled for .NET) | Removed; System.Web uses source-generated regexes | |
| Microsoft Report Viewer 12.0 (decompiled, never built) | Removed | |
| `machine.config` templates copied from a .NET Framework installation | Microsoft's descriptive text removed; what remains is the functional configuration (section, handler and module declarations), adapted to webForCore's assemblies | |
| Unused copies of .NET Framework config files, `GlobalSuppressions` files with no original | Removed | |
| BundleTransformer and the old website (git submodules with mixed-licence sample assets, not built) | Removed from the repository | |

How this was checked: every source file in the port without a licence header was compared, line by line, with the
Reference Source. Files that match it are Microsoft's MIT code with the header missing; files that don't were traced
to their origin and replaced or rewritten. The full list of lifted files and changes is in
[lifted-changes.md](lifted-changes.md), and each lifted file's upstream source is in `tools/lift/manifests/`.
