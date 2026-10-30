# Licensing: what's under which licence, and commercial use

**Short answer.** Almost all of webForCore is under three permissive open-source licences: MIT, Apache 2.0 and
BSD 3-Clause. All three allow commercial use: you may use, modify and ship the code in commercial, closed-source
products, for free, as long as you keep the copyright and licence notices. **However, a small number of files
inherited from WebFormsForCore, the project webForCore forked, came from Microsoft products that aren't open source.
Some of them are compiled into the .NET 11 build.** They're listed under [Not yet cleared](#not-yet-cleared), each with
a fix. Until those are resolved, a commercial distribution of webForCore's binaries shouldn't rely on the summary
above alone.

This page summarises; [NOTICE](../NOTICE) is the authoritative list of components and copyright holders. It isn't
legal advice: for a product decision, have counsel review NOTICE and the items below.

## Which parts have which licence

| Part | Where | Licence | Commercial use |
| --- | --- | --- | --- |
| webForCore's own code (hosting layer, IIS modules, PseudoAppDomain, DPAPI emulation, System.Drawing engine, tools, docs, tests) | throughout; files marked `webForCore` | MIT | ✅ Yes |
| WebFormsForCore's port of System.Web, System.Configuration, System.Web.Extensions, System.Web.Services, Mobile, DataVisualization (from Microsoft's .NET Framework Reference Source) | `src/WebFormsForCore.*` | MIT (Microsoft's Reference Source licence; port © Simon Jakob Egli) | ✅ Yes, except the files under [Not yet cleared](#not-yet-cleared) |
| ASP.NET MVC 5, Web API 2, Web Pages, Razor, Helpers, CORS, MVC Futures | `src/webForCore.System.Web.*`, `src/webForCore.System.Net.Http.Formatting`, `src/webForCore.Microsoft.Web.Mvc` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| Katana (Microsoft.Owin and friends) | `src/webForCore.Microsoft.Owin*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| SignalR 2 | `src/webForCore.Microsoft.AspNet.SignalR.*` | Apache 2.0 (© .NET Foundation) | ✅ Yes |
| ASP.NET Identity 2 | `src/webForCore.Microsoft.AspNet.Identity.*` | MIT (© Microsoft) | ✅ Yes |
| System.Drawing.Common 6.0 | `src/webForCore.System.Drawing.Common` | MIT (© .NET Foundation) | ✅ Yes |
| ASP.NET Web Optimization, WebGrease, Microsoft Ajax Minifier | `src/WebFormsForCore.Web.Optimization*`, `src/WebFormsForCore.WebGrease` | Apache 2.0 (© Microsoft) | ✅ Yes |
| AjaxControlToolkit | `src/WebFormsForCore.AjaxControlToolkit` | BSD 3-Clause (© CodePlex Foundation) | ✅ Yes |
| Redesigner (build-time designer file generator) | `src/WebFormsForCore.Build/Redesigner` | BSD 3-Clause (© Sean Werkema) | ✅ Yes |
| BinaryFormatter port, FontConverter (from .NET); Mono code; SoapFormatter | `src/WebFormsForCore.Serialization.Formatters`, parts of `src/WebFormsForCore.Web`, `src/WebFormsForCore.Drawing` | MIT | ✅ Yes |
| Roslyn compilers (binaries) | `src/WebFormsForCore.Compilers` | MIT (© .NET Foundation) | ✅ Yes |
| Dependencies from NuGet: SkiaSharp, EntityFramework 6, Newtonsoft.Json, CoreWCF, System.Data.SqlClient, ... | restored at build time | MIT or Apache 2.0, each package's own | ✅ Yes |
| Tests lifted from Microsoft and Mono | `test/webForCore.*` | Apache 2.0 or MIT | ✅ Yes (not shipped in packages) |
| BundleTransformer | `src/BundleTransformer` (git submodule, not built) | Apache 2.0, with bundled tools under their own licences | Not part of the build |

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

## Not yet cleared

These files came with the WebFormsForCore fork base. Their origins are Microsoft binaries or products that aren't
under an open-source licence, so the licences above don't cover them. "In the .NET 11 build" means they're compiled
or packed into what an app on Linux uses.

| Item | In the .NET 11 build? | Why it isn't covered | Fix |
| --- | --- | --- | --- |
| `webengine.dll`, `webengine4.dll` (`src/WebFormsForCore.Web/WebFormsForCore/NativeDlls/runtimes/win-*`) | **Packed** into the System.Web NuGet package (`runtimes/win-*`); used only on Windows | .NET Framework 4.8 native binaries, under the .NET Framework licence | Stop packing them and remove them from the repository; Linux never loads them |
| Microsoft.Web.Infrastructure (`src/WebFormsForCore.Web.Infrastructure`, 13 compiled files) | **Yes** | Decompiled from the Microsoft.Web.Infrastructure 2.0.0 package, which is under Microsoft's .NET Library EULA (no source was published) | Rewrite it. It's small: `DynamicModuleUtility.RegisterModule` and the infrastructure helpers |
| System.Web.RegularExpressions (`src/WebFormsForCore.Web.RegularExpressions`, 31 files compiled on .NET 11; 50 more are .NET Framework only) | **Yes** | Decompiled from the regex classes Microsoft generated into System.Web.RegularExpressions.dll; the Reference Source doesn't include them | Compile them for .NET Framework only. On .NET, System.Web already parses with webForCore's source-generated `NetCoreRegexes` |
| `DataProtector.cs` (`src/WebFormsForCore.Web/WebFormsForCore/`) | **Yes** | Its header says it was decompiled from .NET Framework's System.Security.dll | Rewrite it (one abstract base class) |
| Parts of System.Configuration (about 60 files without a header or a same-named Reference Source file, mostly `System.Net.Configuration` and the settings attributes) and of System.Web.ApplicationServices (3 files) | **Yes** | Appear to be decompiled from .NET Framework assemblies. Most of these types do exist in the MIT Reference Source, possibly in differently named files | Review file by file; replace each with its Reference Source original where one exists, and rewrite the rest |
| `machine.config`, `web.config` templates (`src/WebFormsForCore.Configuration/Config`) | **Yes** (embedded) | Copied from a .NET Framework installation; no licence stated | Low risk (configuration data), but confirm or regenerate them |
| `src/WebFormsForCore.Web.Reporting` | No (the solution doesn't build it) | Decompiled from Microsoft Report Viewer 12.0, a proprietary product | Remove it from the repository |
| BundleTransformer sample assets (a CC BY-ND font, an MVC 3 `vsdoc` script, images) | No (submodule, not built) | Mixed or unknown licences | None needed unless the samples are redistributed |

webForCore itself hasn't added any of this material. The files it lifted are all from Microsoft's open-source
repositories, under the licences in the table above, and every lifted file is traceable to its upstream commit
([lifted-changes.md](lifted-changes.md), `tools/lift/manifests/`).

Once the items marked **Yes** and the packed `webengine` binaries are dealt with, everything in the .NET 11 build is
under MIT, Apache 2.0 or BSD 3-Clause.
