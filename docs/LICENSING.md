# Licensing: what's under which licence, and commercial use

**Short answer.** All of webForCore's code is under three permissive open-source licences: MIT, Apache 2.0 and
BSD 3-Clause. All three allow commercial use: you may use, modify and ship the code in commercial, closed-source
products, for free, as long as you keep the copyright and licence notices. The material WebFormsForCore (the project
webForCore forked) had copied or decompiled from closed Microsoft products has been replaced or removed; see
[What was replaced](#what-was-replaced).
One dependency isn't open source: on Windows, SqlClient's `sni.dll` is under Microsoft's .NET Library License, which
allows redistributing it ([below](#which-parts-have-which-licence)). And one set of strings doesn't come from an
open-source source: the messages of LINQ to SQL's errors that referencesource doesn't include, read as data from .NET
Framework's `System.Data.Linq.dll` ([below](#linq-to-sqls-error-messages)).

This page summarises; [NOTICE](../NOTICE) is the authoritative list of components and copyright holders. It isn't
legal advice: for a product decision, have counsel review NOTICE and this page.

## Which parts have which licence

| Part | Where | Licence | Commercial use |
| --- | --- | --- | --- |
| webForCore's own code (hosting layer, IIS modules, webforcore-host, DPAPI emulation, Microsoft.Web.Infrastructure, generated resource accessors, tools, docs, tests) | throughout; files marked `webForCore` | MIT | ✅ Yes |
| WebFormsForCore's port of System.Web, System.Configuration, System.Web.ApplicationServices, System.Web.Extensions, System.Web.Services, Mobile, DataVisualization and aspnet_compiler, from Microsoft's .NET Framework Reference Source | `src/WebFormsForCore.*` (the Reference Source originals come from the `external/referencesource` submodule) | MIT (Microsoft's Reference Source; port © Simon Jakob Egli) | ✅ Yes |
| ASP.NET MVC 5, Web API 2, Web Pages, Razor, Helpers, CORS, Web API tracing, MVC Futures | `src/webForCore.System.Web.*`, `src/webForCore.System.Net.Http.Formatting`, `src/webForCore.Microsoft.Web.Mvc` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| Katana (Microsoft.Owin and friends) | `src/webForCore.Microsoft.Owin*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| SignalR 2 | `src/webForCore.Microsoft.AspNet.SignalR.*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| ASP.NET Identity 2 | `src/webForCore.Microsoft.AspNet.Identity.*` | MIT (© Microsoft) | ✅ Yes |
| LINQ to SQL, System.Data.Linq (from the Reference Source) | `src/webForCore.System.Data.Linq` (the source comes from the `external/referencesource` submodule) | MIT (© Microsoft) | ✅ Yes |
| LINQ to SQL's error messages and exception types for System.Data.Linq.Mapping and .SqlClient (210 strings), read from .NET Framework 4.8's `System.Data.Linq.dll` | `src/webForCore.System.Data.Linq/FrameworkData`, and the resources generated from it | Microsoft's text, from a .NET Framework binary: not under an open-source licence | See [below](#linq-to-sqls-error-messages) |
| System.Data.SqlClient 4.9.1 (from dotnet/maintenance-packages) | `src/webForCore.System.Data.SqlClient` (the source comes from the `external/maintenance-packages` submodule) | MIT (© .NET Foundation) | ✅ Yes |
| ResX reader and writer (from dotnet/winforms) | `src/WebFormsForCore.Web/WebFormsForCore/System.Resources/winforms` | MIT (© .NET Foundation) | ✅ Yes |
| StronglyTypedResourceBuilder (from Mono); Mono code in System.Web, WebGrease and the SoapFormatter | `src/WebFormsForCore.Web/WebFormsForCore/mono` and others | MIT (© Novell and others) | ✅ Yes |
| BinaryFormatter port (from .NET) | `src/WebFormsForCore.Serialization.Formatters` | MIT (© .NET Foundation) | ✅ Yes |
| ASP.NET Web Optimization, WebGrease, Microsoft Ajax Minifier | `src/WebFormsForCore.Web.Optimization*`, `src/WebFormsForCore.WebGrease` | Apache 2.0 (© Microsoft) | ✅ Yes |
| Redesigner (build-time designer file generator) | `src/WebFormsForCore.Build/Redesigner` | BSD 3-Clause (© Sean Werkema) | ✅ Yes |
| Roslyn compilers (binaries) | `src/WebFormsForCore.Compilers` | MIT (© .NET Foundation) | ✅ Yes |
| System.Drawing and System.Drawing.Common 6.0 with a GDI+ engine on SkiaSharp: [drawingForCore](https://github.com/xforcore/drawingForCore)'s packages | restored at build time | MIT (© .NET Foundation and drawingForCore's contributors) | ✅ Yes |
| Dependencies from NuGet: EntityFramework 6, Newtonsoft.Json, CoreWCF, ... | restored at build time | MIT or Apache 2.0, each package's own | ✅ Yes |
| SQL Server Network Interface (`sni.dll`, Windows only), from the `runtime.native.System.Data.SqlClient.sni` 4.4.0 package that `webForCore.Data.SqlClient` depends on, as Microsoft's System.Data.SqlClient package does | restored at build time; copied to the output on Windows | Microsoft .NET Library License: **not open source**, but it may be redistributed in the object code form as part of your programs, under its distribution terms (`dotnet_library_license.txt` in the package) | ✅ Yes, under those terms. On Linux SqlClient uses its managed network code and `sni.dll` isn't used |
| Tests from Microsoft and Mono (submodules) | `test/webForCore.*` | Apache 2.0 or MIT | ✅ Yes (not shipped in packages) |

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

The test suites need one thing that isn't in the repository: SQL Server for the Identity and LINQ to SQL tests, which
runs as a container (Developer edition, not shipped).

### LINQ to SQL's error messages

LINQ to SQL's code is Microsoft's MIT Reference Source, but the classes Microsoft's build generated for its errors and
strings aren't in it, and neither are the strings of System.Data.Linq.Mapping and System.Data.Linq.SqlClient (only
System.Data.Linq's 70, in `System.Data.Linq.txt`). So that applications get .NET Framework's exceptions and messages
(#142), webForCore read them as data from .NET Framework 4.8's `System.Data.Linq.dll` on a Windows CI runner
(`tools/linq/framework_data.cs`): the resource names and message texts, and each error's name, parameters and
exception type. No code or IL was copied; webForCore's generator writes its own classes from the data. The 210
message texts are Microsoft's, from a product under the .NET Framework's licence rather than an open-source one, and
they are what an application sees in exception messages, as on .NET Framework. Have counsel review this part before
relying on it in a product.

### aspnet_regsql's SQL scripts

The SQL scripts that create the database of ASP.NET's SQL providers and SQL Server session state
(18 files: `InstallCommon.sql`, `InstallMembership.sql`, `InstallSqlState.sql` and the other `Install` and `Uninstall`
scripts) ship with .NET Framework in its folder (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`) and aren't in the Reference
Source. So that `SqlServices` and `webforcore-regsql` create the same database as on IIS (#123), System.Web carries
.NET Framework 4.8's files unchanged, read as data on a Windows CI runner (`src/WebFormsForCore.Web/FrameworkData/Sql`),
and the gate tests compare with two scripts .NET Framework's `aspnet_regsql.exe` exported
(`test/webForCore.IIS.Gate.Tests/FrameworkData`); CI's `regsql-framework-data` job reads them again. They are
Microsoft's, under the .NET Framework's licence rather than an open-source one (each one's header says "Copyright Microsoft,
Inc." and "All Rights Reserved."). Have counsel review this part before relying on it in a product.

## What was replaced

WebFormsForCore's fork base contained material that no open licence covered: binaries from .NET Framework, and code
decompiled from .NET Framework assemblies and other Microsoft products. webForCore replaced every piece. Where
Microsoft published the original source under MIT, the decompiled copy was replaced with that original, now compiled
from a submodule of Microsoft's repository with each change as a commit ([MODIFICATIONS.md](MODIFICATIONS.md)). Where
no MIT source exists, webForCore wrote the code from the documented public API.

| Was | Now | How |
| --- | --- | --- |
| `webengine.dll`, `webengine4.dll` (.NET Framework native binaries) | Removed | Hashing uses managed code on every OS |
| System.Configuration: ~150 files decompiled from System.Configuration.dll and System.dll | The Reference Source originals (89 files) | `external/referencesource` |
| Configuration builders, `WindowsAuthenticationElement` | Written from the documented API | No MIT source exists |
| System.Web.ApplicationServices (decompiled) | The Reference Source originals (21 files) | `external/referencesource` |
| aspnet_compiler's front end (decompiled aspnet_compiler.exe) | The Reference Source's `aspnet_compiler.cs` and resources, plus WebFormsForCore's own additions | `external/referencesource` |
| ResX classes (decompiled System.Windows.Forms.dll) | dotnet/winforms' originals (MIT) | `external/winforms` |
| StronglyTypedResourceBuilder (System.Design API reconstruction) | Mono's implementation (MIT) | `external/mono` |
| `BindableTypeAttribute` | The Reference Source original | `external/referencesource` |
| Microsoft.Web.Infrastructure (decompiled from the EULA-licensed 2.0.0 package) | Written from the documented API | Four files |
| `DataProtector` (decompiled from System.Security.dll), `CallContext`, `AppDomainManager`, `UrlBuilder`, `TransactionOption` | Written from the documented API | |
| Resource accessor classes (`SR`, `Res`, decompiled from build-generated code) | Generated from the projects' own `.resx` files | Generated once (`gen_sr.py`, in git history before #61) |
| Assembly identity constants (`AssemblyRef`, `ThisAssembly`, decompiled) | Rewritten with the identity strings they hold | |
| System.Web.RegularExpressions classes (decompiled; never compiled for .NET) | Removed; System.Web uses source-generated regexes | |
| Microsoft Report Viewer 12.0 (decompiled, never built) | Removed | |
| `machine.config` templates copied from a .NET Framework installation | Microsoft's descriptive text removed; what remains is the functional configuration (section, handler and module declarations), adapted to webForCore's assemblies | |
| Unused copies of .NET Framework config files, `GlobalSuppressions` files with no original | Removed | |
| BundleTransformer and the old website (git submodules with mixed-licence sample assets, not built) | Removed from the repository | |

How this was checked: every source file in the port without a licence header was compared, line by line, with the
Reference Source. Files that match it are Microsoft's MIT code with the header missing; files that don't were traced
to their origin and replaced or rewritten. Microsoft's and Mono's files now come from the `external/` submodules, and
every change to them is a commit on the fork's `webforcore` branch ([MODIFICATIONS.md](MODIFICATIONS.md)).
