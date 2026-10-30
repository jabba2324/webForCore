# LIFT.md

Record of everything lifted into or changed in webForCore, and of each phase gate.
Plan: `WEBFORCORE-LIFT-HANDOFF.md` (planning doc, 29 Oct 2026).

**Target framework: net11.0 only.** net8.0 and net10.0 were dropped on 29 Oct 2026 (the handoff said
net10.0 first). net48 stays for the TestApp's .NET Framework build (the future IIS comparison) and
netstandard2.0 stays for the MSBuild task DLL. Staying mergeable with upstream is not a goal; fork files
are changed wherever that is the cleaner fix.

## Sources

| Repo | Commit | Local path | Use |
| --- | --- | --- | --- |
| webformsforcore/WebFormsForCore | `52f4172` (29 Oct 2026) | `webForCore/` (branch `webforcore-lift`) | Fork base |
| simonegli8/WebFormsForCore.AjaxControlToolkit (submodule) | `0c5b111` | `src/WebFormsForCore.AjaxControlToolkit` | Upstream submodule, locally modified |
| simonegli8/BundleTransformer (submodule) | `1661843` | `src/BundleTransformer` | Upstream submodule, unused by the solution |
| aspnet/AspNetWebStack | `2106565` (7 Oct 2026) | `../AspNetWebStack` (read-only) | Phase 2 lift source |
| aspnet/AspNetKatana | `096e225` (7 Oct 2026) | `../AspNetKatana` (read-only) | Phase 3 lift source |

The GitHub fork (`webForCore` under the owner's account) is not created yet; `origin` still points at upstream.
Nothing is committed yet.

Only three submodules are pinned in the tree (BundleTransformer, AjaxControlToolkit, `www`). The other
`.gitmodules` entries (dotnet/runtime, mono, referencesource, ...) are stale and nothing in the build uses
them. Mono is not needed: the only "Mono" dependency is the Mono.Cecil NuGet library used by
`FakeStrongName.targets`.

## Environment (Fedora 44, x64)

- SDK `11.0.100-rc.3.26522.148` in `~/.dotnet` (via `dotnet-install.sh`), matching `global.json`.
  Fedora's packaged `11.0.100-rc.1` is rejected by `global.json` (roll-forward never goes rc.3 to rc.1).
- `OPENSSL_ENABLE_SHA1_SIGNATURES=1` is required. Fedora's DEFAULT crypto policy rejects SHA-1
  signatures, and strong-name signing is RSA-SHA1, so `csc` crashes with
  `OpenSslCryptographicException: error:03000098:digital envelope routines::invalid digest`.
  The variable is Fedora/RHEL OpenSSL specific and affects only the process it is set on.
- The .NET 8 and 10 runtimes also in `~/.dotnet` were needed only while the repo multi-targeted; they can
  be removed.

## Build and run

```
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH OPENSSL_ENABLE_SHA1_SIGNATURES=1
cd webForCore
git submodule update --init src/BundleTransformer src/WebFormsForCore.AjaxControlToolkit
dotnet build WebFormsForCore.slnx -c Debug
dotnet run --project src/WebFormsForCore.TestApp/WebFormsForCore.TestApp.csproj --no-build -c Debug -f net11.0 --no-launch-profile
```

The TestApp binds `http://localhost:5000` from its own Kestrel configuration and ignores `--urls`.
By default it now builds against this tree (`UseLocalBuild=true`); `-p:UseLocalBuild=false` restores
upstream's nuget.org `1.7.0-rc3` packages, which then also need
`ROSLYN_COMPILER_LOCATION=$PWD/src/WebFormsForCore.Compilers/Roslyn.net11.0` (packaging bug, see below).

Debug and Release both build. (Until Phase 2, Release failed on `WebFormsForCore.Web.DataVisualization`,
which had no reference to System.Web; it is fixed, see Phase 2.)

When checking a "clean" build, run `dotnet build-server shutdown` first or pass `-nodeReuse:false`. Reused
MSBuild nodes cache imported `.targets` files, which once made a build without `lib/` look like it passed.

### Build result (29 Oct 2026, clean tree, `-nodeReuse:false`)

`dotnet build WebFormsForCore.slnx -c Debug`: **0 errors**, 26 outputs, all net11.0 apart from the net48
builds of the TestApp, AjaxControlToolkit and WebFormsForCore.Build plus the netstandard2.0 task DLL.
Log: `../logs/44-sln-truly-clean.log`.

### TestApp results (Linux, net11.0, this tree's System.Web)

- `bin_dotnet/System.Web.dll` is byte-identical to `src/WebFormsForCore.Web/bin/Debug/net11.0/System.Web.dll`;
  `csc.dll` is present; error-page stack traces point at `.../webForCore/src/WebFormsForCore.Web/...`.
- `/`, `/Default.aspx`, `/About.aspx`, `/Contact.aspx`, `/Test.aspx`, `/SessionTest.aspx`, `/AjaxTest.aspx`:
  301 to the FriendlyUrls form, then 200 with `__VIEWSTATE` and no error markers. First request about
  1.5 s (Global.asax compile), then a few ms to about 80 ms.
- `/favicon.ico`, `/Content/Site.css`: 200. `/NoSuchPage.aspx`: 404 (ASP.NET error page).
- SessionTest postbacks (script: Increment x2, Save Note, fresh GET): counter 0, 1, 2; the note round-trips;
  the session persists. ViewState, event dispatch, post data and Session all work. The session ID changes
  between the first GET and the first postback, as on IIS when nothing is stored yet.

## Phase 1 gate

- TestApp serves pages on Linux under net11.0, on this tree's System.Web: **met** (29 Oct 2026).
- Build command and results recorded: **met** (this file).
- Not yet done: the GitHub fork, and committing.

## Changes to fork files

### Linux build fixes

| File(s) | Change | Why |
| --- | --- | --- |
| 13 csproj files with a `ChangeAliasesOfNugetRefs` target (Web, Services, Infrastructure, Extensions, Mobile, Optimization, Optimization.WebForms, WebGrease, DynamicData, TestApp; submodule: AjaxControlToolkit, HtmlEditor.Sanitizer) | In the `ReferencePath Remove` regexes: `\\dotnet\\` became `[\\/]\.?dotnet[\\/]`, and `configurationmanager\\` became `configurationmanager[\\/]` (32 occurrences) | The regexes matched only Windows separators, so on Linux the framework's `System.Web.dll` facade and the NuGet `System.Configuration.ConfigurationManager` were never removed, causing CS0433 and CS7069 in consumers. `\.?` also covers a user-local SDK in `~/.dotnet`. |
| `WebFormsForCore.slnx` | The Build project is no longer excluded from `Debug\|Any CPU` | All 13 strong-named projects `Import` `lib/WebFormsForCore.Build/netstandard2.0/FakeStrongName.targets` unconditionally, and only the Build project creates `lib/`. Upstream's Debug build works only if `lib/` is left over from an earlier build. |
| `src/WebFormsForCore.AjaxControlToolkit/AjaxControlToolkit/AjaxControlToolkit.csproj` (submodule) | `PostBuildEvent` PropertyGroup gets `Condition="'$(OS)' == 'Windows_NT'"` | Runs `$(TargetDir)LinkStaticResources` (no such file in the tree) and Windows `copy` with unbalanced quotes; fails on Linux after the DLL is built. |
| `src/WebFormsForCore.Build/CreateAspDesignerFiles.cs` | Out-of-process designer generation runs `dotnet` (not `dotnet.exe`) when not on Windows | `dotnet.exe` does not exist on Linux. |

### TestApp builds against this tree

`src/WebFormsForCore.TestApp/WebFormsForCore.TestApp.csproj`:

- New `UseLocalBuild` property, default `true`. When true, the 10 `WebFormsForCore.*` PackageReferences are
  replaced by 15 ProjectReferences into this tree; the `WebFormsForCore.Build` tooling package stays in both modes.
- New `CopyLocalRoslyn` target copies `WebFormsForCore.Compilers/Roslyn.$(TargetFramework)/**` into `bin_dotnet`.
  The Compilers project copies Roslyn into its own bin with a `Copy` task, so a ProjectReference does not carry `csc.dll`.
- New `ReferencePath Remove` for the framework's `System.Web.dll` facade (local mode). Without it FriendlyUrls
  fails with CS7069 (`RouteCollection` "claims it is defined in System.Web"). The existing `ReferencePath Remove`
  for System.Web has a condition (path contains `configurationmanager`) that never matches.

### net11.0 only

- `<TargetFrameworks>` in all 24 solution projects: `net8.0` and `net10.0` removed (now `net11.0`, or
  `net48;net11.0` / `net48;net11.0;netstandard2.0` where they were there before).
- 24 `ItemGroup`/`PropertyGroup` blocks conditioned only on net8.0/net10.0 removed from 12 csproj files;
  3 `OR` conditions reduced to net11.0; 9 net8.0/net10.0 packing items removed (scratchpad script
  `drop_old_tfms.py`, applied once).
- `src/WebFormsForCore.Compilers`: removed `Roslyn.net8.0/` and `Roslyn.net10.0/` (100 tracked files, 27 MB).
  Packing now ships `Roslyn.net11.0/**` to `lib/net11.0`, which fixes for our packages the upstream packaging
  bug below. Removed the `#if NET8_0` branch in `Compiler.cs` (`DefaultAnalyzerAssemblyLoader`).
- `src/WebFormsForCore.Build`:
  - `AspNetCoreCompiler.cs`: the out-of-process precompiler path was hard-coded to `net10.0` and now uses
    `net11.0`. With net10.0 gone it would have pointed at a DLL that is no longer built. `IsNet10` became
    `IsNet11`, and `NET10_0_OR_GREATER` became `NET11_0_OR_GREATER`.
  - `CreateAspDesignerFiles.cs`: net8/9/10 branches replaced by net11.0. The in-process path previously threw
    `NotSupportedException` for net11.0, and for net8.0 because of a missing `else`.
  - `FakeStrongName.cs` and the inline copy in `FakeStrongName.targets`: the runtime/TFM fallback is `net11.0`.
- `src/WebFormsForCore.Web/FileChangesMonitor.cs`: removed the `#if NET8_0 || NET9_0` block that disabled
  file-change notifications on non-Windows. No behaviour change on net11.0, where watchers were already on.
  Note for later: on Linux each watched directory uses inotify watches (`fs.inotify.max_user_watches`).
- `src/WebFormsForCore.AspNetCompiler/Properties/launchSettings.json`: `-t net48,net10.0` became `net48,net11.0`.
- Left as they are: `BinaryCompatibility`/`VersionUtil`/`ClientBuildManager` version tables (data about the
  app's declared framework), `NET8_0_OR_GREATER`/`NET10_0_OR_GREATER` checks (true on net11.0), the
  checked-in TestApp `*.deps.json` files (not generated artefacts, as first written here: they are hand-maintained and load-bearing, see "deps.json and the framework facades" below), and BundleTransformer and NativeDlls (not in the solution).

Side effects of upstream's own build steps (not edits of ours): the build rewrites
`src/WebFormsForCore.Configuration/Properties/AssemblyInfo.cs` and sets the executable bit on
`src/WebFormsForCore.TestApp/Scripts/jquery-3.7.1*`.

## Upstream issues found (for reporting)

1. `WebFormsForCore.Compilers 1.7.0-rc3` on nuget.org ships `csc.dll` and Roslyn only in `lib/net8.0`
   (28 files); `lib/net10.0` and `lib/net11.0` contain just `WebFormsForCore.Compilers.dll`, because only
   `Roslyn.net8.0\**` had `Pack=True`. Every net10/net11 app on that package returns 500
   (`FileNotFoundException: .../bin_dotnet/csc.exe`) on its first dynamic request, on any OS.
2. The 13 `ReferencePath Remove` regexes are Windows-only (fixed here).
3. Debug builds depend on a leftover `lib/` because the Build project is excluded from Debug (fixed here).
4. `AspNetCoreCompiler` hard-codes `net10.0`, and `CreateAspDesignerFiles` throws for net11.0 targets (fixed here).
5. `WebFormsForCore.Web.DataVisualization` had no System.Web reference and was skipped in Debug (fixed in Phase 2).
6. On Linux, `ConfigurationManager` failed in every non-web process (fixed in Phase 2).

## Phase 2: compile spike and the first lifted libraries (30 Oct 2026)

### Tools (tools/lift/)

| File | Purpose |
| --- | --- |
| `gen_csproj.py` | From the handoff, net11.0. Generates SDK-style spike projects that point at the untouched Web Stack checkout. Now also handles test projects. |
| `spike.sh` | From the handoff, net11.0. Builds the fork baseline, generates and builds 12 Web Stack projects, groups errors. Run from outside the fork. |
| `group_errors.py` | From the handoff. Groups compile errors by project and missing API. |
| `lift_project.py` | New. Copies a project into the fork as a real project (details below). |
| `check_assembly.cs` | New (`dotnet run tools/lift/check_assembly.cs -- <dll>...`). Prints the assembly identity and loads every strongly typed resource class, reading one string from each. |
| `trx_summary.py` | New. Summarises `dotnet test` .trx results: totals, and failures grouped by class and message. |
| `spike-results/`, `test-results/`, `manifests/` | Committed spike report, test results, and per-file provenance (source path, destination path) for every lifted file. |

Changes to the handoff's scripts, and why:

- `net11.0` throughout. `Microsoft.Owin` is 4.2.3 (the handoff's 4.2.2 was from memory; 4.2.3 is the latest on nuget.org),
  and `System.Runtime.Caching` is `11.0.0-rc.3.26522.148` (handoff: 9.0.0).
- Every generated project gets `System.CodeDom` (`11.0.0-rc.3.26522.148`). On .NET Framework those types came with `System.dll`;
  without the package, Razor had 27 errors (CS1069 `CodeCompileUnit`, `CodeLinePragma`).
- PackageId bug: dropping both `System.` and `Microsoft.` gave Microsoft.Web.Mvc and System.Web.Mvc the same id ("Ambiguous project name").
  Only `System.` is dropped now.
- `System.Web.WebPages.Deployment` added to the project list; WebPages references it.
- `spike.sh` writes a `Directory.Build.targets` that removes the framework's `System.Web.dll` facade and the NuGet
  `System.Configuration.ConfigurationManager` (both duplicate fork assemblies, CS7069/CS0433). Error counts are unique errors.
- `group_errors.py` accepts the `::TargetFramework=` suffix on project names.

### Spike result (final run)

| Project | Errors |
| --- | --- |
| System.Web.Razor, System.Web.Http, System.Web.Cors, System.Web.Http.Cors, System.Web.Http.Owin, System.Web.Http.WebHost | 0 |
| System.Web.WebPages.Deployment | 5: `AppDomainHelper` creates an AppDomain (`AppDomainSetup()`, `ApplicationBase`, `ConfigurationFile`, `PrivateBinPath`, `AppDomain.Evidence`) |
| System.Web.Helpers, WebPages, WebPages.Razor, Mvc, Microsoft.Web.Mvc | Blocked behind Deployment. WebPages also has 1 of its own: `System.Data.Linq.Binary` in `Html/HtmlHelper.Input.cs` |

Report: `tools/lift/spike-results/`. First run: Razor 27 errors, Helpers 2,123 (all in the fork's DataVisualization),
Microsoft.Web.Mvc a restore failure (PackageId collision). Generator report: UNMAPPED `System.Data.Linq` (Http, WebPages, Mvc,
Microsoft.Web.Mvc), `System.Data.Entity` (Mvc), `System.ServiceModel` and `System.ServiceModel.Web` (Microsoft.Web.Mvc).
Http compiles despite its `System.Data.Linq` reference (nothing in it uses the assembly).

**Spike assemblies have wrong resource names**, as the handoff predicted: the project-specific resources were named
`System.Web.Razor.RazorResources` while `RazorResources.Designer.cs` asks for `System.Web.Razor.Resources.RazorResources`
(likewise `SRResources` in Http, Cors and Http.Cors). They compile but throw on the first resource lookup; `check_assembly.cs`
reports FAIL on them. The spike projects live outside the source tree, so MSBuild names resources after the file only.
The spike assemblies are also unsigned (PublicKeyToken null).

### Lifted projects

| Project in the fork | Assembly | Version | Target | Built from |
| --- | --- | --- | --- | --- |
| `src/webForCore.System.Net.Http.Formatting` | System.Net.Http.Formatting | 6.0.1.0 | netstandard2.0 | `src/System.Net.Http.Formatting.ns2_0` |
| `src/webForCore.System.Web.Razor` | System.Web.Razor | 3.0.0.0 | net11.0 | `src/System.Web.Razor` |
| `src/webForCore.System.Web.Http` | System.Web.Http | 5.3.1.0 | net11.0 | `src/System.Web.Http` |
| `src/webForCore.System.Web.Cors` | System.Web.Cors | 5.3.1.0 | net11.0 | `src/System.Web.Cors` |
| `src/webForCore.System.Web.Http.Cors` | System.Web.Http.Cors | 5.3.1.0 | net11.0 | `src/System.Web.Http.Cors` |
| `test/webForCore.Microsoft.TestCommon` | Microsoft.TestCommon | | net11.0 | `test/Microsoft.TestCommon` (its net8.0 item set) |
| `test/webForCore.System.Net.Http.Formatting.Test` | System.Net.Http.Formatting.ns2_0.Test | | net11.0 | `test/System.Net.Http.Formatting.ns2_0.Test` |
| `test/webForCore.System.Web.{Razor,Http,Cors,Http.Cors}.Test` | System.Web.*.Test | | net11.0 | `test/System.Web.*.Test` |

All are in `WebFormsForCore.slnx` (folders `/WebStack/` and `/WebStack.Tests/`). Each builds with 0 errors.

How `lift_project.py` copies, and why:

- **Source of truth is MSBuild.** It evaluates the spike project (or Microsoft's own SDK-style project) and copies exactly the
  evaluated `Compile`, `EmbeddedResource` and copied `None` items. The csproj lists them explicitly.
- **Original layout.** Files keep their path relative to the original project; shared files (`src/Common`,
  `CommonAssemblyInfo.cs`) go to their `Link` path from Microsoft's csproj. Shared files are duplicated per project, as linked
  files were compiled into each assembly.
- **Resource names.** Each `.resx` gets an explicit `LogicalName`: the literal its `Designer.cs` passes to `ResourceManager`, or
  RootNamespace + folder + name (CommonResources/CommonWebApiResources look themselves up by suffix). Other resources (Razor test
  baselines) get no `LogicalName`; MSBuild computes the original names from the restored layout. `check_assembly.cs` reads
  every resource class in all five libraries successfully.
- **Identity.** Delay signed with Microsoft's public key `tools/35MSSharedLib1024.snk` from the Web Stack repo (copied to
  `src/SigningKey/`), exactly as `tools/WebStack.settings.targets` does. Resulting PublicKeyToken: `31bf3856ad364e35`, as shipped.
  .NET does not verify strong-name signatures, so the assemblies load normally. This replaces the handoff's "strong-named with the
  fork's key": the Web Stack's public key is in its repo, and Microsoft's identity is what packages compiled against Web API and MVC
  reference.
- **Same compiler settings as Microsoft:** `LangVersion` 9.0 and the original `DefineConstants` (`ASPNETWEBAPI`, `ASPNETMVC`,
  `ASPNETWEBPAGES`, `ASPNETHTTPFORMATTING`); `TreatWarningsAsErrors` off.
- **CRLF line endings.** Microsoft builds on Windows, where the Web Stack's `* text` .gitattributes gives CRLF. Every lifted text file
  is written with CRLF, so string literals compile as in Microsoft's binaries. `.gitattributes` marks `src/webForCore.*` and
  `test/webForCore.*` `-text` so git never converts them.
- **Formatting stays netstandard2.0.** Microsoft ships the ns2_0 build to .NET Core consumers. Retargeting to net11.0 would switch
  `TranscodingStream`'s cancellation path and six `NETCOREAPP` blocks to code Microsoft does not ship. It runs on .NET 11 unchanged.
- **Test projects** use Microsoft's xunit 2.9.3 and Moq 4.21.0, with `Microsoft.NET.Test.Sdk` 18.11.0 and `xunit.runner.visualstudio`
  3.1.5 (Microsoft's 16.11.0 and 2.4.5 predate .NET 11). `XunitXml.TestLogger` is dropped.
- `src/webForCore.WebStack.targets` (imported by every lifted project) removes the framework `System.Web.dll` facade and the NuGet
  `System.Configuration.ConfigurationManager`, as the fork's own projects do.

Edits to Microsoft's code (all as `FIXUPS` in `lift_project.py`, applied after copying, each required to match exactly):

| File | Change | Why |
| --- | --- | --- |
| `test/webForCore.System.Web.Http.Test/Controllers/AuthenticationFilterResultTests.cs` | `CancellationToken cancellationToken;` becomes `= default;` (3 places) | CS0165: .NET reference assemblies expose the struct's private field, so it is no longer treated as empty; .NET Framework's did not. Test code only. |
| `test/webForCore.System.Web.Http.Test/Validation/Validators/DataAnnotationsModelValidatorTest.cs` | Two tests `[Fact(Skip = ...)]`, body under `#if NETFRAMEWORK` | They use `AppDomainUtils.RunInSeparateAppDomain` to isolate a static switch. Microsoft's TestCommon has no AppDomain helper outside .NET Framework, so Microsoft runs them on .NET Framework only. Running them inline would leak the static into parallel tests. |

No product (src) file was edited.

### Fork changes made in Phase 2

| File | Change | Why |
| --- | --- | --- |
| `src/WebFormsForCore.Configuration/System/System.Configuration/ClientConfigPaths.cs` | On non-Windows use `Assembly.Location` for the exe path; null-guard the web.config probe | `file:///home/x` lost its leading `/` and `/` was replaced by `\`, so the path collapsed to one file name and **every non-web process on Linux** threw `ConfigurationErrorsException` ("Configuration system failed to initialize", `ArgumentNullException: path1`) on first `ConfigurationManager` use. |
| `src/WebFormsForCore.Configuration/System/System.Configuration/ClientConfigurationHost.cs` | `MachineConfigFilePath` getter goes through the setter | Only the web host called the setter that extracts the embedded machine.config, so other processes had none ("The configuration section 'appSettings' has an unexpected declaration"). The test host now writes `App_Data/machine.config` next to its binaries, as the web host does under the app root. |
| `src/WebFormsForCore.Web.DataVisualization/*.csproj` | ProjectReferences to System.Web and System.Drawing (alias `WfcDrawing`), linked `AssemblyRef.cs`, `System.Data.SqlClient` 4.9.1, the fork's reference-cleanup target | It had no references at all. 2,123 errors, then 473 (CS0182 cascade from `Editors` constants), then 27, then 0. The fork's attribute-only System.Drawing is aliased because the chart code needs `System.Drawing.Common` (`Graphics`, `Bitmap`), and the two collide on `FontConverter` and `ToolboxBitmapAttribute`. |
| `src/WebFormsForCore.Web.DataVisualization/ThisAssembly.cs` (new) | Assembly constants (Version 4.0.0.0) | Used by `Common/EditorNames.cs`; the other reference-source projects have one each. |
| `src/WebFormsForCore.Web.DataVisualization/WebForm/AssemblyInfo.cs` | `AssemblyRef` qualified as `System.Web.AssemblyRef` | The file has no namespace, so the name bound to an inaccessible global `AssemblyRef` elsewhere. |
| `src/WebFormsForCore.Web.DataVisualization/Common/DataManager/DataSeries.cs` | `#if !SQLRS_CONTROL` at line 593 became `#if false` with a comment | The block uses a stale field name and a `FontColor` property that does not exist, so Microsoft's build cannot have compiled it. Shipped behaviour: empty points are **not** forced transparent. (Renaming the field would have changed chart output.) `SQLRS_CONTROL` is not defined, because it also switches the design-time dummy-data seed. File encoding (Windows-1252) preserved. |
| `WebFormsForCore.slnx` | DataVisualization built in Debug again; `/WebStack/` and `/WebStack.Tests/` folders | |
| `LICENSE-APACHE-2.0.txt`, `NOTICE`, `.gitattributes` (new) | Full Apache 2.0 text (from apache.org; Microsoft's `LICENSE.txt` is the short notice, reproduced in NOTICE), attribution, CRLF protection | Ground rules: keep licence headers, add NOTICE entries. |

`dotnet build WebFormsForCore.slnx -c Debug -nodeReuse:false`: 0 errors, 38 outputs. TestApp re-checked after these changes:
all pages 200, 404 for an unknown page, SessionTest postback sequence unchanged.

### Unit tests (net11.0, Linux), first run

Superseded by "Test gap: newlines and the 15 unclassified failures" below.

| Suite | Total | Passed | Failed | Skipped | Pass rate |
| --- | --- | --- | --- | --- | --- |
| System.Web.Cors.Test | 72 | 72 | 0 | 0 | 100.0% |
| System.Web.Http.Cors.Test | 124 | 124 | 0 | 0 | 100.0% |
| System.Web.Http.Test | 3,157 | 3,144 | 11 | 2 | 99.6% |
| System.Net.Http.Formatting.Test | 6,445 | 6,312 | 133 | 0 | 97.9% |
| System.Web.Razor.Test | 1,460 | 1,175 | 285 | 0 | 80.5% |

Details: `tools/lift/test-results/2026-10-30.md`. Run: `dotnet test test/webForCore.<Name>/webForCore.<Name>.csproj`.

Failures by cause (classified from the .trx messages):

| Cause | Razor | Formatting | Http |
| --- | --- | --- | --- |
| `Environment.NewLine` is `\n` on Linux; tests and baselines expect Windows `\r\n` (generated code, parser span lengths, messages) | 278 | 34 | 1 |
| .NET's `ContentDispositionHeaderValue.Name` is unquoted (`N0`); tests expect the quoted .NET Framework value (`"N0"`) | | 91 | |
| Exception message format: `(Parameter 'x')` vs `\nParameter name: x` (3); reworded `ArgumentOutOfRangeException` (1) | | | 4 |
| Other .NET vs .NET Framework differences: `Activator` accepts types Framework rejected (2); `MediaTypeHeaderValue` throws `ArgumentNullException` not `ArgumentException` (1); `System.Private.CoreLib` vs `mscorlib` in a type name (1); `double` parsing accepts `1.79769313486232E+308` as Infinity, so `FloatRouteConstraint` matches it (1); duplicate-controller message (1) | | | 6 |
| Not yet classified. Razor: 6 partial-parsing tests (`ImplicitExpressionAccepts*`, Assert.Equal on values) and 1 string-at-EOL error test. Formatting: XML/JSON indentation checks (3), `MultipartFileStreamProvider` invalid root path on Linux (2), invalid Unicode fix-up, Moq setup, buffer-size message | 7 | 8 | |

None of the failures traced so far is a lift defect. The resource-name and `ConfigurationManager` failures found on the way
were fixed (Razor went from 286 to 285 failures after the resource fix; Http from 49 to 11 after the Configuration fix).
Parity items for later: the Linux newline in product output; ContentDisposition quoting; `FloatRouteConstraint` on overflow.

### Phase 2 gate

- Razor, Web API core (System.Web.Http with System.Net.Http.Formatting) and CORS compile on net11.0: **met**.
- Their unit tests run: **met**. Pass rates above.
- Spike report committed: written to `tools/lift/spike-results/`; **not committed yet** (nothing in this tree is committed).

## Test gap: newlines and the 15 unclassified failures (30 Oct 2026)

**Newline policy (decided):** product code keeps the platform newline (`\n` on Linux); only tests change. Tests that
were written for Windows `\r\n` either get Windows input (where they hard-code positions) or compare newline-agnostically
(where they compare product output). All edits are in `lift_project.py` (`TRANSFORMS` and `FIXUPS`), applied on every
lift; re-lifting the three projects reproduced the hand-edited files byte for byte, apart from two added comments.

| Project | Edit | Why |
| --- | --- | --- |
| System.Web.Razor.Test | `Environment.NewLine` becomes `"\r\n"` in all test sources (569 uses, 35 files) | Parser tests build input with `Environment.NewLine` but hard-code `\r\n` spans and offsets such as `new TextChange(12, ...)`; on Linux every edit landed one character early. |
| System.Web.Razor.Test | The two test `StringWriter`s used for CodeDom output get `NewLine = "\r\n"` | Test harness, not product: reproduces the Windows CodeDom output the baselines were recorded from. |
| System.Web.Razor.Test | `MiscUtils.StripRuntimeVersion` removes the whole `Runtime Version:` header line (`//` and VB `'`) and is applied to baselines too; new `NormalizeGeneratedCode` also ignores `\r\n` vs `\n`; both code-generator comparisons use it | .NET's System.CodeDom no longer writes the `Runtime Version:` line. Razor's generator itself writes `Environment.NewLine` into some snippets (`__o = ;`, `#line hidden`), which is product output. |
| Microsoft.TestCommon | `VerifyExceptionMessage` compares messages newline-agnostically | .NET builds messages such as `ArgumentOutOfRangeException`'s "Actual value was" with `Environment.NewLine`; the expected texts use `\r\n`. |
| System.Net.Http.Formatting.Test | Indentation checks look for `\n`, not `\r\n` (7 checks in 4 files) | Indented XML and JSON uses the platform newline. The 4 negative checks ("no indentation by default") had been passing vacuously on Linux. |

Microsoft's TestCommon already handles .NET's `(Parameter 'x')` message format (`#if NETCOREAPP3_1_OR_GREATER`); the
remaining parameter-format failures are tests that build the message by hand.

**The 15 previously unclassified failures, traced:**

| Count | Test(s) | Cause | Product behaviour differs? |
| --- | --- | --- | --- |
| 7 | Razor `*PartialParsingTest.ImplicitExpressionAccepts*` (6), `CSharpErrorTest.ParseBlockTerminatesNormalCSharpStringsAtEOLIfEndQuoteMissing` | Newline: input from `Environment.NewLine`, positions for `\r\n` | No (fixed by the transform) |
| 3 | `*MediaTypeFormatterTests.*_Indent` | Indented XML/JSON written with the platform newline | Yes: indented bodies use `\n` on Linux (kept, per the policy) |
| 2 | `HttpContentMultipartExtensionsTests.ReadAsMultipartAsync_ThrowsOnInvalidBufferSize`, `ByteRangeStreamContentTest.Ctor_ThrowsOnInvalidBufferSize` | Newline inside .NET's exception message | Message text only (fixed by the TestCommon change) |
| 2 | `MultipartFileStreamProviderTests.Constructor_ThrowsOnInvalidRootPath(" ")`, `("  ")` | `" "` is a valid directory name on Linux; Windows rejects it | Yes, edge case: a whitespace upload root is accepted |
| 1 | `JsonNetSerializationTest.InvalidUnicodeStringsAreFixedUp` | Since .NET Core 3.0, invalid UTF-8 is replaced per Unicode best practice (9 U+FFFD instead of 6) | Yes: JSON with malformed UTF-8 decodes differently |
| 1 | `HttpContentExtensionsTest.ReadAsAsyncOfT_ReadsFromContent_ThenInvokesFormattersReadFromStreamMethod` | Test asserts the formatter receives the same stream object `SerializeToStreamAsync` wrote; .NET 11's `HttpContent` buffering (`LimitArrayPoolWriteStream`) hands a different `MemoryStream` over the same data | No: implementation detail |

**Results after these changes** (`tools/lift/test-results/2026-10-30.md`):

| Suite | Total | Passed | Failed | Skipped | Pass rate | First run |
| --- | --- | --- | --- | --- | --- | --- |
| System.Web.Cors.Test | 72 | 72 | 0 | 0 | 100.0% | 100.0% |
| System.Web.Http.Cors.Test | 124 | 124 | 0 | 0 | 100.0% | 100.0% |
| System.Web.Razor.Test | 1,460 | 1,460 | 0 | 0 | 100.0% | 80.5% |
| System.Web.Http.Test | 3,157 | 3,145 | 10 | 2 | 99.6% | 99.6% |
| System.Net.Http.Formatting.Test | 6,445 | 6,350 | 95 | 0 | 98.5% | 97.9% |

All 105 remaining failures are .NET vs .NET Framework (or Linux vs Windows) differences, none of them newlines:
ContentDisposition quoting (91), parameter-name message format in hand-built expectations (3), reworded
`ArgumentOutOfRangeException` (1), `Activator` (2), `MediaTypeHeaderValue` null check type (1), `System.Private.CoreLib` in a type
name (1), `FloatRouteConstraint` overflow (1), duplicate-controller message (1), whitespace upload root (2), UTF-8 replacement (1),
HttpContent stream identity (1). Not changed: whether to match .NET Framework behaviour there is a parity decision.

## deps.json and the framework facades (30 Oct 2026)

The shared framework (Microsoft.NETCore.App) contains facades named `System.Web.dll`, `System.Drawing.dll` and
`System.Configuration.dll`. When an app also carries a `System.Web.dll`, the host keeps the copy with the higher
`assemblyVersion` in `deps.json`. The SDK writes no `assemblyVersion` for project references, so the framework facade
(4.0.0.0) won, and loading any fork System.Web type failed with "Could not load file or assembly 'System.Web,
Version=11.0.0.0 ...' The requested assembly version conflicts with what is already bound" (0x80131053).

- The TestApp avoids this only because it copies a **hand-maintained** `WebFormsForCore.TestApp.deps.json`
  (`CopyToOutputDirectory=Always`) that gives System.Web `assemblyVersion` 8.0.0.0. The checked-in `*.deps.json` files in
  `src/WebFormsForCore.TestApp/` are therefore load-bearing.
- System.Web.Http.Test had passed because its tests never load System.Web types at run time. Phase 3 (Web API on
  System.Web) would have hit it.
- **Fix:** `src/webForCore.WebStack.targets` now runs after `GenerateBuildDependencyFile` and sets the real
  `assemblyVersion` (read from the built DLL) for every app-local assembly that the Microsoft.NETCore.App or
  Microsoft.AspNetCore.App reference packs also contain. In the test hosts: System.Web 11.0.0.0, System.Drawing 13.5.0.0.
  All Web Stack suites gave identical results after the change.
- Not yet done: fork apps (the TestApp) still use the hand-maintained file; they could import the same target instead.

## Mono System.Web tests, stage 1 (30 Oct 2026)

Source: mono/mono `0f53e9e` (archived 2024-08-27), sparse checkout of `mcs/class/System.Web/Test`, `mcs/class/test-helpers`,
`System.Web_test.dll.sources` and `LICENSE` in `../mono`. Licence: MIT (each test file carries Novell's MIT header; Mono's
LICENSE covers the class libraries). Tool: `tools/lift/lift_mono_tests.py`; project `test/webForCore.System.Web.MonoTests`
(assembly name `System.Web_test`, as in Mono), in the solution under `/WebStack.Tests/`.

File selection, starting from the 563 existing files in Mono's `System.Web_test.dll.sources`:

| Files | Status | Reason |
| --- | --- | --- |
| 188 | **included** (186 tests + `test-helpers/NunitHelpers.cs`, `TestResourceHelper.cs`) | |
| 103 | deferred | use Mono's in-process web harness (`MonoTests.SystemWeb.Framework`: `WebTest`, `NunitWeb`) |
| 11 | deferred | use `MonoTests.stand_alone.WebHarness` (HTML comparison) from `mainsoft/` |
| 45 | deferred | `mainsoft/` itself (harness, HtmlAgilityPack); third-party, licence not reviewed |
| 206 + 3 | excluded | `*Cas.cs` and `Cas/`: Code Access Security tests; .NET does not enforce CAS |
| 4 | excluded | Mono internals or AppDomains: `CacheItemPriorityQueueTest`, `MachineKeySectionUtilsTest`, `UrlUtilsTest` (`SystemWebTestShim`), `ApplicationHostTest` (inspects the created AppDomain's `AppDomainSetup`) |
| 1 | deferred | `RequestValidatorTest`: needs `BaseFakeHttpWorkerRequest` from the harness-using `HttpRequestTest.cs` |

Adaptations (all in `lift_mono_tests.py`; files are otherwise byte for byte):

- NUnit 3.14.0 (the last version with NUnit 2's classic `Assert` in the main namespace; 10,058 `Assert.AreEqual` uses),
  NUnit3TestAdapter 4.6.0, Microsoft.NET.Test.Sdk 18.11.0.
- `NUnit2Compat.cs`: `[ExpectedException(typeof(X))]` (removed in NUnit 3) via `IWrapTestMethod`, with NUnit 2 semantics
  (exact type); a no-op `MonoTODOAttribute`.
- Transforms: `TestFixtureSetUp` to `OneTimeSetUp` (3); `protected internal override` (and `internal` / `protected override`
  across a line break) to `protected override` (17): Mono's System.Web let its tests override `protected internal` members
  as `protected internal`, which C# rejects from another assembly (CS0507); `Is.StringContaining` to `Does.Contain` (1).
- `TARGET_DOTNET` and `DOT_NET` defined: where Mono wrote a .NET variant of a test, that variant compiles.
- References: the fork's System.Web, Extensions, Services, ApplicationServices, Configuration, Drawing;
  `System.Data.SqlClient` 4.9.1.

Stage 1 contains 5 `NotDotNet` tests (Mono behaviour that differs from .NET) and 18 `NotWorking` tests (Mono failed them,
.NET should pass).

**Result:** 2,634 tests: **2,557 passed (97.1%)**, 71 failed, 6 not run (`[Ignore]` in Mono's source). Details in
`tools/lift/test-results/2026-10-30.md`. Failures by cause (first pass, not yet checked against Microsoft's reference source):

| Count | Cause | Kind |
| --- | --- | --- |
| 16 | `SqlDataSourceView`: `DbProviderFactories` has no `System.Data.SqlClient` registration on .NET | Fork gap (apps using `SqlDataSource`) |
| 8 | `MembershipUserCollection` throws `PlatformNotSupportedException` ("not supported on the .NET Framework Client Profile") | Fork stub |
| 4 | `MachineKey.Encode`, `LosFormatter` with MAC: `DllNotFoundException` for `webengine4.dll` (IIS's native crypto) | Fork gap on Linux |
| 3 | `windows-1252` and other code pages not registered (`HtmlEncode_XSS` x2, `UrlDecode_Bug507666`) | Fork/runtime gap (`CodePagesEncodingProvider`) |
| 2 | `HttpCookie` and output-cache serialisation use .NET's removed BinaryFormatter | Fork gap |
| 5 | `SimpleWorkerRequest.MapPath` / AppDomain tests: "requires Windows NT" | Windows-only API |
| 7 | `HttpWriterTest`: `NullReferenceException` in `OneTimeSetUp` | To investigate |
| 2 | `ConfigurationErrorsException` "Value cannot be null" when using the cache outside hosting | Fork (config system, non-hosted) |
| 24 | Assertion mismatches and other exceptions (`HttpUtility` encode/decode, `Unit`, validators' rendering, `FormsIdentity` null checks, `HttpResponse` headers, output stream bounds, `ObjectDataSource`, `Debug.Fail`) | To check per test against Microsoft's reference source: test expects Mono behaviour, or fork deviates |

## Fork fixes found by the Mono tests, and Mono tests corrected to .NET behaviour (30 Oct 2026)

Ground truth for both: Microsoft's .NET Framework 4.8 reference source (microsoft/referencesource `ec9fa9a`, MIT),
sparse checkout of `System.Web`, `System.Web.ApplicationServices` and `System/net/System/Net/WebUtility.cs` in
`../referencesource`. Where the fork's code is identical to it, the fork's behaviour is .NET Framework's, and a
differing Mono expectation is corrected; where the fork differs, the fork is fixed.

The Mono suite now runs in **Release**, as Microsoft ships: in Debug, `Debug.Fail` calls in Microsoft's code fired
(`DataSourceSelectArgumentsTest.RaiseUnsupportedCapabilitiesError`).

### Fork fixes (product code)

| Area | File(s) | Bug | Fix |
| --- | --- | --- | --- |
| Machine key crypto | `WebFormsForCore.Web/UnsafeNativeMethods.cs` | With SHA1 validation, `MachineKeySection` (identical to Microsoft's) computes HMAC-SHA1 and SHA1 in IIS's native `webengine4.dll`, which does not exist on Linux: `MachineKey.Encode`, `LosFormatter`/ViewState MAC threw `DllNotFoundException` | `GetHMACSHA1Hash`/`GetSHA1Hash` stay native on Windows and are managed elsewhere: `SHA1(outer || SHA1(inner || data || modifier))` from Microsoft's pads. Checked equal to `HMACSHA1` on 200 random cases. |
| Membership | `WebFormsForCore.Web.ApplicationServices/Util/SystemWebProxy.cs` | Looked up `"System.Web.Security.MembershipAdapter, WebFormsForCore.Web"`; no assembly has that name, so every `MembershipUser` and membership password call threw `PlatformNotSupportedException` | Assembly name `System.Web`, as Microsoft's |
| Membership | `WebFormsForCore.Web.ApplicationServices/Properties/AssemblyInfo.cs` | `InternalsVisibleTo("System.Web")` named only the fork's key; FakeStrongName gives the built System.Web Microsoft's key, so `MembershipAdapter` failed to load ("attempting to implement an inaccessible interface") | Also grant Microsoft's key (b03f5f7f11d50a3a), as RegularExpressions already does. A scan of all `InternalsVisibleTo` (with `#if` evaluated) found no other fork-key-only grant. |
| Type resolution | 30 files, 51 sites (`BuildManager`, `HttpProfileBase`, `Roles`, `Membership`, `UrlAuthorizationModule`, `WebConfigurationHost`, resx, CodeDom, ...) | `Type.GetType(name, asmResolver, (asm, n, ic) => asm?.GetType(n, ...))`: for a name without an assembly (`"System.Guid"`, profile `type="System.String"`) `asm` is null, so the type was never found. Microsoft calls `Type.GetType(name)`, which searches the calling assembly and the core library. (Found by `DataListTest.DataSourceID`.) | `asm != null ? asm.GetType(...) : global::System.Type.GetType(...)` with the same flags |
| Wrong assembly names | `BrowserCapabilitiesFactory.cs`, `ConfigurationManagerInternalFactory.cs` | Menu adapter keyed `"..., WebFormsForCore.Web"` (never matched, so the MenuAdapter was never applied); `ConfigurationManagerInternal` looked up in `WebFormsForCore.Configuration` (application settings would throw) | Menu key `typeof(Menu).AssemblyQualifiedName` (the fork's equivalent of Microsoft's `"..., System.Web, Version=4.0.0.0, ..."`); `System.Configuration.ConfigurationManager` |
| Configuration outside hosting | `WebFormsForCore.Web/Configuration/HttpConfigurationSystem.cs` | Machine config directory was `Path.Combine(HttpRuntime.AppDomainAppPath, "App_Data")`; `AppDomainAppPath` is null outside hosting, so `HttpRuntime.Cache`, `OutputCache`, `WebConfigurationManager` threw ("Value cannot be null (path1)") | Directory of `ClientConfigurationHost.MachineConfigFilePath` (the same `App_Data` folder under hosting; extracted next to the binaries otherwise) |
| Code pages, data providers | `WebFormsForCore.Web/WebFormsForCore/FrameworkDefaults.cs` (new) | .NET Framework has all Windows code pages and registers SqlClient, OleDb, ODBC (and Oracle) in machine.config; .NET has neither, so `requestEncoding="windows-1252"` and `SqlDataSource ProviderName="System.Data.SqlClient"` failed | A `[ModuleInitializer]` in System.Web registers `CodePagesEncodingProvider` and, by type name and only if not already registered, `System.Data.SqlClient` and `Microsoft.Data.SqlClient` (both to Microsoft.Data.SqlClient, as the fork's `SqlDataSource` uses), `System.Data.OleDb`, `System.Data.Odbc`. Providers an app declares in its own `<system.data><DbProviderFactories>` are not read yet. |

### Mono tests corrected to .NET Framework behaviour

All in `lift_mono_tests.py` (`FIXUPS`, `METHODS`), each with the reference-source evidence in a comment.

| Test(s) | Mono expected | .NET Framework (reference source) |
| --- | --- | --- |
| `HttpWriterTest` fixture | `new HttpContext(null)` usable | Throws `NullReferenceException` (`HttpContext.Init` -> `CookielessHelper`); use `SimpleWorkerRequest`'s 5-argument constructor |
| `HttpWriterTest.NullBufferException` | `ArgumentOutOfRangeException` | `ArgumentNullException` (HttpWriter.cs:1625, Dev10 507392) |
| `HttpResponseOutputStreamTest.Write_Count_Overflow`, `Write_Offset_Overflow` | count clamped; `ArgumentOutOfRangeException` | `ArgumentException` when offset + count exceeds the buffer; offset == length with count 0 accepted (HttpWriter.cs:721-729) |
| `LosFormatterTest.Serialize_Output_Null` (NotDotNet) | `ArgumentNullException` | `NullReferenceException`, as Mono's own comment says (LOSFormatter.cs:127) |
| `FormsIdentityTest.Null`, `Null_Name` | null ticket accepted / NRE on Name | `ArgumentNullException("ticket")` (FormsIdentity.cs:68) |
| `UnitTest.UnitConstructors` B2, `IncorrectConstructor9` | exact 0.9; `"34.4px"` throws | value stored as Single (0.9f); fractional pixels truncated to 34 (Unit.cs) |
| `BaseValidatorTest` D6, `RangeValidatorTest` R1, `ValidationSummaryTest` D3, R2-R5, R7 | default `ForeColor` Red | Red only when `RenderingCompatibility < 4.0` (BaseValidator.cs:557) |
| `HttpResponseTest.ClearHeaders`, `WriteHeaders` #C3 | `X-AspNet-Version` = `Environment.Version` (Mono: 4.0.30319) | `4.0.30319` (System.Web version without its last part, HttpRuntimeSection.cs) |
| `HttpUtilityTest.JavaScriptStringEncode` | `&`, U+0085, U+2028, U+2029 not encoded | encoded (`CharRequiresJavaScriptEncoding`, HttpEncoder.cs:85) |
| `HttpUtilityTest.HtmlEncode_XSS`, `HttpServerUtilityTest.HtmlEncode_XSS` (NotDotNet) | full-width `＜script＞` encoded | not encoded; Microsoft declined to change it (Mono's comment) |
| `DataBindingCollectionTest.ChangeTest` | `Clear()` silent; `Remove` of an absent binding raises `Changed` | `Clear()` raises `Changed`; `Remove` raises it only if the binding is present |

Test-environment additions: `WebForCoreTestEnvironment` (`[SetUpFixture]`) runs System.Web's module initializer before any
test, since some tests call `Encoding.GetEncoding("windows-1251")` before touching System.Web.

### Results (Release)

| Suite | Total | Passed | Failed | Skipped | Pass rate |
| --- | --- | --- | --- | --- | --- |
| System.Web.MonoTests | 2,634 | 2,598 | 30 | 6 | 98.6% (stage 1 start: 97.1%) |
| System.Web.Http.Test | 3,157 | 3,145 | 10 | 2 | 99.6% |
| System.Net.Http.Formatting.Test | 6,445 | 6,350 | 95 | 0 | 98.5% |
| System.Web.Razor.Test, Cors.Test, Http.Cors.Test | 1,460 / 72 / 124 | all | 0 | 0 | 100% |

TestApp re-checked on the Release build: all pages, 404, SessionTest postbacks unchanged.

Remaining Mono failures (30):

| Count | Tests | Status |
| --- | --- | --- |
| 16 | `SqlDataSourceViewTest.*` | **Decision**: the fork builds Microsoft.Data.SqlClient commands where .NET Framework builds System.Data.SqlClient ones; the tests cast to `System.Data.SqlClient.SqlParameterCollection` |
| 4 | `HttpUtilityTest.HtmlDecode`, `HtmlDecode_String_TextWriter`, `HtmlEncode` (lone surrogate), `UrlPathEncode` (U+007F) | **Decision**: on .NET the fork type-forwards `System.Web.HttpUtility` to .NET's `System.Web.HttpUtility.dll` (`HttpServerUtility.cs:19`; Microsoft's HttpUtility is compiled only for .NET Framework), so `HttpUtility` behaves as .NET's, and a custom `HttpEncoder` (`<httpRuntime encoderType>`) is not used by it. Microsoft's `UrlPathEncode` gives `%7f`; .NET's leaves U+007F. `HtmlDecode("&#000;")` and lone surrogates follow .NET's WebUtility (strict), which matches .NET Framework apps targeting 4.5+, not legacy apps without `targetFramework` (4.0 compat). |
| 5 | `SimpleWorkerRequestTest.AppDomain*` | Windows-only: `ApplicationHost.CreateApplicationHost` throws `PlatformNotSupportedException` off Windows (needs an AppDomain) |
| 2 | `HttpCookieTest.CookieValuesSerialization`, `OutputCacheTest.Deserialize` | The tests themselves call .NET's BinaryFormatter, removed in .NET 9 |
| 3 | `HtmlInputRadioButtonTest.RenderValue1`, `HttpClientCertificateTest.Valid` (dates 2 ticks apart), `OutputCacheTest.Serialize` (one byte differs) | Not yet investigated |

Parity gaps recorded, not fixed: application-defined `<system.data><DbProviderFactories>` entries are not registered;
`WebUtility`'s 4.0-compat decoding (no `targetFramework`) is not emulated.

## Parity decisions applied (30 Oct 2026)

Decided by the project owner: the reference behaviour is .NET Framework 4.8 with `<httpRuntime targetFramework>` 4.5 or
later (4.0-compat quirks of apps without targetFramework are a documented gap); Microsoft's `HttpUtility`, not .NET's;
`System.Data.SqlClient`, not Microsoft.Data.SqlClient.

### Fork changes

| Change | Files | Notes |
| --- | --- | --- |
| **System.Data.SqlClient** instead of Microsoft.Data.SqlClient | 13 source files (`using`), 4 (`SqlTypes`), `WebFormsForCore.Web.csproj`, `WebFormsForCore.Web.Extensions.csproj` (`System.Data.SqlClient` 4.9.1), `FrameworkDefaults.cs` | `SqlDataSource`, SQL membership/roles/profile/session/personalization/web events/cache dependency now build System.Data.SqlClient objects, as on .NET Framework. The 16 `SqlDataSourceViewTest` tests pass unchanged. |
| **Microsoft's HttpUtility** compiled on .NET | `HttpServerUtility.cs` (no type forward; `HttpUtility` and its `JavaScriptStringEncode` no longer under `#if NETFRAMEWORK`), `IHtmlString.cs`, 17 `HttpUtilityInternal.` references back to `HttpUtility.` | .NET's `System.Web.HttpUtility.dll` holds exactly `HttpUtility` and `IHtmlString`; both now come from the fork's System.Web, as on .NET Framework. `HttpUtility` goes through `HttpEncoder.Current` (so `<httpRuntime encoderType>` applies). |
| Compile-time: drop .NET's HttpUtility | `Directory.Build.targets` (new, repo root) | In projects with a project reference to this repo's System.Web, the framework's `System.Web.HttpUtility.dll` is removed from the references (CS0433 otherwise). Projects using upstream's NuGet System.Web (WebFormsForCore.Build) keep .NET's. |
| Run-time page compilation | `WebFormsForCore.Configuration/Config/machine.config` | `<add assembly="System.Web.HttpUtility" />` removed from `<compilation><assemblies>`; with it, every .aspx failed with CS0433. |
| machine.config refresh | `ClientConfigurationHost.cs` | The extracted machine.config was refreshed only when older than `BuildDateAttribute`, which the assembly never carries (AssemblyInfo.cs has no `BuildDate`; the `ReplaceBuildDate` target only rewrites the file, which is the AssemblyInfo.cs churn seen after every build). So an existing machine.config was never updated. It is now rewritten when its content differs. The TestApp's tracked `App_Data/machine.config` was refreshed this way (one line). |

### Mono tests corrected to 4.5+ behaviour

| Test(s) | Correction | Reference source |
| --- | --- | --- |
| `HttpUtilityTest.HtmlDecode`, `HtmlDecode_String_TextWriter` (#B4, #B5) | `&#000;` and `&#x00;` decode to U+0000 | strict decoding conformance, WebUtility.cs:209 |
| `HttpUtilityTest.HtmlEncode` helper | lone surrogate becomes U+FFFD, a valid pair `&#<scalar>;` | strict encoding conformance, WebUtility.cs:123-132 |
| `HtmlInputRadioButtonTest.RenderValue1` | `value="hola<&"` (unencoded) | `WriteAttribute(name, value)` calls `WriteAttribute(name, value, false)`, HTMLTextWriter.cs:1147 |
| `HttpClientCertificateTest.Valid` | ValidFrom and ValidUntil within 1 ms | two separate `DateTime.Now` initialisers, HttpClientCertificate.cs:270-271 (equal only on a coarse clock) |
| `HttpCookieTest`, `OutputCacheTest` | use the fork's BinaryFormatter port (as its System.Web does) instead of .NET's removed one | |
| `SimpleWorkerRequestTest.AppDomain*` (5) | `[Ignore]` with reason | `ApplicationHost.CreateApplicationHost` needs a new AppDomain |

### Results (Release)

| Suite | Total | Passed | Failed | Skipped | Pass rate (of run) |
| --- | --- | --- | --- | --- | --- |
| System.Web.MonoTests | 2,634 | **2,620** | 3 | 11 | 99.9% |
| System.Web.Http.Test | 3,157 | 3,145 | 10 | 2 | 99.7% |
| System.Net.Http.Formatting.Test | 6,445 | 6,350 | 95 | 0 | 98.5% |
| System.Web.Razor.Test, Cors.Test, Http.Cors.Test | 1,460 / 72 / 124 | all | 0 | 0 | 100% |

TestApp (Release): all pages 200, 404 for an unknown page, SessionTest postbacks unchanged, with Microsoft's HttpUtility.

Remaining Mono failures:

| Test | Reason |
| --- | --- |
| `OutputCacheTest.Serialize`, `Deserialize` | Byte baselines recorded on Mono (Mono's serializer and its test assembly `DotNet4_NUnit_Tests`); cannot be checked against .NET Framework without a Windows reference machine |
| `HttpCookieTest.CookieValuesSerialization` | **Fork gap:** .NET 11's `NameObjectCollectionBase.GetObjectData` throws `PlatformNotSupportedException`, so the fork's BinaryFormatter port cannot serialize `NameValueCollection`-based types (`HttpCookie.Values`, `HttpValueCollection`). Fix: a serialization surrogate in the port that writes .NET Framework's `GetObjectData` fields, to stay wire-compatible. Not done yet. |

Open parity items: app-defined `<system.data><DbProviderFactories>`; `NameObjectCollectionBase` serialization; the
Web Stack BCL differences (ContentDisposition quoting, exception message formats, `FloatRouteConstraint` overflow, UTF-8
replacement); packages built from this repo will need the HttpUtility reference removal (`Directory.Build.targets`) in
their build targets for consuming apps.

## Web Stack platform differences and BinaryFormatter wire compatibility (30 Oct 2026)

### Web Stack: .NET Framework behaviour restored in Web API's own code (`FIXUPS` in `lift_project.py`)

| Change | File | .NET Framework 4.8 | .NET 11 (before) |
| --- | --- | --- | --- |
| Overflow rejected | `System.Web.Http` `Routing/Constraints/FloatRouteConstraint.cs`, `DoubleRouteConstraint.cs` | `Single/Double.TryParse` returns false on overflow; an infinite result only for the infinity symbols | returns true with ±Infinity, so `{x:float}` matched `1.79769313486232E+300` |
| Whitespace root path rejected | `System.Net.Http.Formatting` `MultipartFileStreamProvider.cs` | `Path.GetFullPath("  ")` throws `ArgumentException` ("The path is not of a legal form.") | accepted on Linux; uploads would be written to `<cwd>/  ` |

### Web Stack: accepted platform differences (tests corrected to .NET's behaviour)

These come from the .NET BCL, not from Web API. Application code can see the ones marked *.

| Difference | Tests | Note |
| --- | --- | --- |
| \* `ContentDispositionHeaderValue.FileName` is unquoted (.NET Framework returned `"name"` with quotes) | `HttpContentMultipartExtensionsTests` (91 cases) | Web API's own providers unquote `FileName` themselves (`FormattingUtilities.UnquoteToken`), so `MultipartFormDataStreamProvider` behaves the same. Application code that strips the quotes itself is unaffected; code that relies on them is not. |
| \* `ArgumentException.Message` format: `msg (Parameter 'name')` (.NET Framework: `msg` + newline + `Parameter name: name`) | `MutableObjectModelBinderTest` (2), `HttpFilterCollectionTest` | Visible in model-state error messages built from setter exceptions |
| `List<T>.InsertRange` out-of-range message reworded ("less than or equal to the size") | `DefaultServicesTests.InsertRange_GuardClauses` | |
| \* `MediaTypeHeaderValue(null)` throws `ArgumentNullException` (.NET Framework: `ArgumentException`) | `HttpRequestMessageExtensionsTest` | Catching `ArgumentException` still works (it is the base class) |
| Core library is `System.Private.CoreLib`, not `mscorlib`, in assembly-qualified names | `ReflectedHttpActionDescriptorTest` | Now built from `typeof(Task<Task>).FullName` |
| `System.Uri` has a private parameterless constructor that `Expression.New` uses | `TypeActivatorTest` (2) | Test type changed to `StringContent`, which has none on either platform |
| Controller-ambiguity message uses `Environment.NewLine` | `DefaultHttpControllerSelectorTest` | Newline policy (tests only) |
| UTF-8 decoder replaces each byte of an invalid sequence (Unicode "maximal subpart"; .NET Core 3.0+) | `JsonNetSerializationTest.InvalidUnicodeStringsAreFixedUp` | 3 U+FFFD per lone surrogate |
| `HttpContent.ReadAsStreamAsync` returns a different stream over its internal buffer | `HttpContentExtensionsTest` | Test checks the formatter is called once with the content |

### BinaryFormatter port: .NET Framework types .NET no longer serializes

`src/WebFormsForCore.Serialization.Formatters/Serialization/Formatters/Binary/FrameworkSurrogates.cs` (new) adds built-in
surrogates, which `BinaryFormatter` consults after the caller's `SurrogateSelector`
(`FrameworkSurrogateSelector`, wired in `BinaryFormatter.Core.cs`).

* **`NameObjectCollectionBase` and subclasses** (`NameValueCollection`, System.Web's `HttpValueCollection` (so
  `HttpCookie.Values`, `Request.Form` copies), and application subclasses marked `[Serializable]`). .NET's `GetObjectData`,
  serialization constructor and `OnDeserialization` all throw `PlatformNotSupportedException`. The surrogate writes
  .NET Framework's `GetObjectData` members (`ReadOnly`, `HashProvider`/`Comparer` or `KeyComparer`, `Count`, `Keys`,
  `Values`, `Version`, from referencesource `nameobjectcollectionbase.cs`) and, as .NET Framework did, fills the collection
  in the deserialization callback (`ObjectManager.RegisterObject` registers the surrogate's callback instead of the
  throwing one). The default comparer is written as .NET Framework writes it: the V1 invariant
  `CaseInsensitiveHashCodeProvider` + `CaseInsensitiveComparer`; on reading those, the collection gets
  `StringComparer.InvariantCultureIgnoreCase`. Not handled: subclasses that override `GetObjectData` or
  `OnDeserialization` (their overrides call .NET's throwing base); derived-type field initializers do not run on
  deserialization (no serialization constructor is called).
* **`CaseInsensitiveComparer`, `CaseInsensitiveHashCodeProvider`, `TextInfo`**: not serializable on .NET; written with
  .NET Framework's fields (`m_compareInfo`; `m_text`; TextInfo's `m_listSeparator` … `m_win32LangID`), read back as the
  same culture's instances. Also makes .NET Framework `Hashtable`s with V1 case-insensitive comparers readable.
* **Assembly versions on the wire**: this repository's .NET Framework 4.0 assemblies (System.Web, System.Web.Extensions,
  System.Web.Services, System.Web.ApplicationServices, System.Web.DynamicData, System.Web.Entity, System.Web.Mobile,
  System.Web.RegularExpressions, System.Web.DataVisualization, System.Drawing) carry version 11.0.0.0; the port now writes
  them as `Version=4.0.0.0`, as .NET Framework does (`FormatterServices.GetClrAssemblyName`). Reading 4.0.0.0 already
  resolved to them.
* **Exceptions**: `Deserialize` no longer wraps other exceptions in `SerializationException("Invalid BinaryFormatter
  stream")` (a .NET addition); .NET Framework let them through, e.g. the `ArgumentException` of a delegate that no longer
  binds, which `OutputCache.Deserialize` callers see.

Known .NET behaviour left as is: .NET's `CompareInfo` writes `m_name`, `m_SortVersion`, `culture` but not .NET
Framework's `win32LCID`; .NET Framework's reader tolerates the missing field and rebuilds from `m_name`.

Verification:

* `test/webForCore.Serialization.Tests` (new, xunit, 9 tests): round trips (default, OrdinalIgnoreCase, culture and V1
  comparers, read-only, shared and nested references, `HttpCookie.Values`, TextInfo and the V1 comparers) and the wire
  layout (type and member names in .NET Framework's order, `System, Version=4.0.0.0` for NameValueCollection,
  `System.Web, Version=4.0.0.0`, no .NET assembly names).
* The 589 .NET Framework blobs in dotnet/runtime's `BinaryFormatterTestData.cs` (release/8.0): 458 deserialize and
  re-serialize, exactly as before the change; the 131 failures (test types, System.DirectoryServices, EventLog, …) are
  unchanged.
* Mono's `OutputCacheTest.Serialize`/`Deserialize` now pass byte for byte. Their `TARGET_DOTNET` baselines were recorded
  on .NET Framework from a test assembly named `DotNet4_NUnit_Tests` (its name is in the bytes), so the Mono test assembly
  is now built under that name (`lift_mono_tests.py`).
* Not verified: a NameValueCollection stream written by .NET Framework itself (none is available without Windows; the
  dotnet/runtime data has none, as .NET Core never serialized the type). The layout is taken member for member from the
  reference source.

### Results (Release, 30 Oct 2026)

| Suite | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| System.Web.MonoTests (`DotNet4_NUnit_Tests`) | 2,634 | **2,623** | 0 | 11 |
| System.Web.Http.Test | 3,157 | **3,155** | 0 | 2 |
| System.Net.Http.Formatting.Test | 6,445 | **6,445** | 0 | 0 |
| System.Web.Razor.Test, Cors.Test, Http.Cors.Test | 1,460 / 72 / 124 | all | 0 | 0 |
| webForCore.Serialization.Tests (new) | 9 | 9 | 0 | 0 |

The skips are the 2 AppDomain tests in Http.Test (Microsoft runs them on .NET Framework only), and in the Mono tests the
5 `SimpleWorkerRequestTest.AppDomain*` plus Mono's own 6 `[Ignore]`s. TestApp (Release): all pages 200 (after
FriendlyUrls' redirect), 404 for an unknown page, SessionTest postbacks and session persistence unchanged.

Open parity items: app-defined `<system.data><DbProviderFactories>`; packages built from this repo need the HttpUtility
reference removal (`Directory.Build.targets`) in their build targets for consuming apps; `NameObjectCollectionBase`
subclasses that override `GetObjectData`/`OnDeserialization`.

## Phase 3: Web API 2 on System.Web, and Katana (30 Oct 2026)

### Lifted

All by `tools/lift/lift_project.py` (manifests in `tools/lift/manifests/`). Katana is read from `../AspNetKatana`
(`096e225`), beside the Web Stack checkout. Its projects are SDK-style net45 projects built with Microsoft's Arcade SDK,
so they are not evaluated with MSBuild: the tool takes the project folder's default globs plus the csproj's explicit
items (linked `Owin.Loader` files, references, `InternalsVisibleTo`). Katana files are copied byte for byte (Katana has
no `.gitattributes`; its files are LF). Katana assemblies get the attributes Arcade generates for Microsoft's build
(version 4.2.3.0, file version 4.200.325.35202, company, copyright, description, `NeutralResourcesLanguage` en-US) and
are delay signed with Microsoft's key, like the Web Stack. `Owin.dll` (`IAppBuilder`) is not in the Katana repo; it
stays the `Owin` 1.0.0 package (net40, pure managed, NU1701).

| Project | Source | Role |
| --- | --- | --- |
| System.Web.Http.WebHost | Web Stack | Web API on System.Web (`GlobalConfiguration`, `HttpControllerHandler`) |
| System.Web.Http.Owin | Web Stack | Web API on OWIN (`app.UseWebApi`) |
| Microsoft.Owin, .Security, .Security.Cookies, .Security.OAuth | Katana | OWIN abstractions, cookie and OAuth bearer middleware (the Web API 2 "individual accounts" template uses both) |
| Microsoft.Owin.Host.SystemWeb | Katana | OWIN server on System.Web (`OwinHttpModule`, integrated-pipeline stages) |
| Microsoft.Owin.Hosting, .Host.HttpListener, .Testing | Katana | For the tests: `WebApp.Start`, the HttpListener server, `TestServer` |

Tests: System.Web.Http.WebHost.Test, System.Web.Http.Owin.Test, Microsoft.Owin.Tests, Microsoft.Owin.Host.SystemWeb.Tests,
Microsoft.Owin.Security.Tests (without the Facebook, Google, Twitter, Microsoft account, OpenID Connect, JWT and Active
Directory tests, whose middleware is not lifted). `HostedUrlHelperTest` (MVC URLs) waits for System.Web.Mvc (phase 4).

**System.Web.Helpers is moved to phase 5:** it needs System.Web.WebPages, which needs System.Web.WebPages.Deployment,
the one spike project that does not compile (AppDomain APIs). Web API does not need it.

### Fixes: .NET Framework behaviour restored in lifted code (`FIXUPS`, `tools/lift/additions/`)

| Where | Problem on .NET | Fix |
| --- | --- | --- |
| WebHost, Owin, Formatting (`ResponseContent.cs`, 11 sites) | `HttpResponseMessage.Content` is never null on .NET (an internal `EmptyContent`); Web API's hosts read null as "no body". With EmptyContent they wrote `Content-Length: 0` (also on 204), made a buffering decision and ran the body copy; OWIN canceled the request; batched responses (`HttpMessageContent`) gained `Content-Length: 0` | `ResponseContent.Get` returns null for EmptyContent; the hosts, buffer policy selectors and `HttpMessageContent` use it |
| WebHost `SeekableBufferedRequestStream` | .NET's `StreamContent` copies with `CopyToAsync`, which `NonOwnedStream` forwards to the inner stream, skipping the switch to `Request.InputStream` at the end of the body (.NET Framework's `StreamContent` read through the stream) | `CopyToAsync` reads through the stream's own `ReadAsync` |
| Microsoft.Owin `HostString.ToUriComponent` | ICU `IdnMapping.GetAscii(s, index, count)` lowercases an ASCII host; `GetAscii(s)` does not. `Mixed.Case:9090` became `mixed.case:9090` | Maps the host part as its own string |
| Host.HttpListener `OwinHttpListener` | Reads .NET Framework's private `HttpListenerRequest` fields unless on Mono; on .NET the read failed and **every request hung** | Takes Katana's Mono paths when the fields are missing (.NET's managed HttpListener came from Mono) |
| Owin.Security `CertificateSubjectPublicKeyInfoValidator` | Reads the CryptoAPI `CERT_CONTEXT` behind `X509Certificate.Handle` (Windows only) | `PublicKey.ExportSubjectPublicKeyInfo()`, the same DER encoding |
| Owin.Security `DpapiDataProtector` | `System.Security.Cryptography.DpapiDataProtector` is .NET Framework only | `ProtectedData` (same DPAPI call; Windows only, PlatformNotSupportedException elsewhere). The entropy is a SHA-256 of the purposes, **not interchangeable with .NET Framework's DPAPI payloads** (its derivation is in System.Security.dll, not in the reference source). On System.Web the host supplies MachineKey protection instead, so apps on the fork do not use DPAPI; **self-hosted OWIN on Linux needs an explicit data protection provider** |
| Host.SystemWeb, Hosting `DefaultLoader` | `AppDomainSetup.PrivateBinPath` does not exist | Scans ApplicationBase, as .NET Framework does without a private bin path (Host.SystemWeb finds startup classes through `BuildManager.GetReferencedAssemblies`) |
| Hosting `HostingStarterFactory` | The "Domain" starter needs AppDomains and remoting | Excluded; asking for it throws PlatformNotSupportedException |
| Hosting | `SettingsLoader` finds `ConfigurationManager` by reflection (in the GAC on .NET Framework) | References the fork's System.Configuration.ConfigurationManager |

### Fork changes (System.Web)

| Change | Files | Why |
| --- | --- | --- |
| `Response.Headers` on the Kestrel host | `HttpResponse.cs`, `HttpHeaderCollection.cs` | Integrated-mode API on IIS (`PlatformNotSupportedException` otherwise); OWIN's System.Web host is built on it. On `AspNetCoreWorkerRequest` it is now a writable view of the response's own header store (what `AppendHeader` writes and the response sends), in both pipeline modes. Other worker requests keep .NET Framework's behaviour |
| IIS integrated mode's Content-Type rule on the Kestrel host | `HttpResponse.cs` | Integrated mode sends Content-Type only when managed code set it, or a handler ran and the body is not empty; the classic path always sent `text/html`, so empty OWIN 302/401/404/409/410 responses had a Content-Type they do not have on IIS |
| `GetServerName` from the Host header | `AspNetCoreWorkerRequest.cs` | IIS's SERVER_NAME is the Host header's host, and `Request.Url` is built from it. The fork returned `localhost` for loopback and the local IP otherwise, so `CreatedAtRoute` gave `Location: http://localhost:5081/...` for a request to `127.0.0.1:5081` (and an IP for named hosts) |

**Pipeline mode (handoff open question 2).** The fork's middleware defaults to the classic pipeline, and its integrated
mode does not run: `HttpApplication` needs the module list IIS reads from `<system.webServer><modules>`, and
`PipelineStepManager` is driven by IIS's native notifications; neither is emulated (`UseIntegratedMode()` fails with a
NullReferenceException in `BuildIntegratedModuleCollection`). Katana's stage events (`AddOnXxxAsync` registered by
`OwinHttpModule`) do run in the fork's classic pipeline, and with `Response.Headers` available OWIN works there. Full
integrated-pipeline emulation (`<system.webServer><modules>`, notification order, `PostXxx` stages) is an open item.

### Accepted platform differences (tests corrected; app-visible ones marked *)

| Test(s) | .NET behaviour |
| --- | --- |
| HostString `FromUriComponentMixedUnicodeThrows` * | .NET's IdnMapping (ICU) decodes a host that mixes Unicode and punycode labels; .NET Framework threw |
| WebHost `Seek_ThrowsOnInvalidSeekOrigin` | `(Parameter 'origin')` message format |
| WebHost `..._When_Formatter_Write_Task_Faults` | Async stack traces name the method, not the state machine's `MoveNext` |
| WebHost `..._WithFailedNegotiation_HandlesWithCustomException` | `response.Content` is EmptyContent, checked through `ResponseContent.Get` |
| WebHost `ClientCertificateGet_ReturnsRequestBaseClientCertificate` | The test certificate (empty OIDs, long-form lengths) is rejected by .NET's DER reader outside Windows; replaced by a minimal valid one |
| Security cookie and OAuth middleware tests (45) | TestServer has no data protection and DPAPI is Windows only; the two test servers get `TestDataProtectionProvider` (AES-CBC + HMAC-SHA256, `tools/lift/additions/`) |

Skipped: Microsoft.Owin.Security.Tests 4 (Katana's own skips, "Uses outdated hash").

### Gate: a stock Web API 2 sample

`test/webForCore.WebApi.Sample`: the Visual Studio "Web API" template (`WebApiConfig`, `Global.asax` with
`GlobalConfiguration.Configure`, `ValuesController`), the "Getting Started with ASP.NET Web API 2" `ProductsController`
(with validation and `CreatedAtRoute`), a `StatusController` for the other status-code paths, and an OWIN `Startup`
(`[assembly: OwinStartup]`) serving the same controllers under `/owin` through `app.UseWebApi`. It runs on Kestrel with
the fork's System.Web like the TestApp. As on IIS, where they are in the GAC, it must deploy System.Web.Services and
System.Web.Extensions: the fork's machine.config is .NET Framework's root configuration (`<compilation><assemblies>`,
`ScriptModule` in `<httpModules>`).

`test/webForCore.WebApi.Gate.Tests` starts the sample and sends 32 requests to each host (64), plus 2 unmatched routes:
GET/POST/PUT/DELETE/HEAD/OPTIONS, JSON and XML (request and response), model-state 400, 201 with Location, 204, 302,
401, 403 HttpError, 404 (Web API and ASP.NET), 405 with Allow, 409, 410, 500 with error detail, attribute routes and
constraints, query binding, UTF-8. **Oracle:** the same requests to the same controllers and `WebApiConfig` in Web API's
in-memory `HttpServer`, which involves no System.Web; status, media type, charset, body, Location and Allow must be
identical (for the 500, everything but the stack trace). The headers System.Web adds are checked against what it sends
on IIS (`X-AspNet-Version`; WebHost `Cache-Control: no-cache`, `Pragma: no-cache`, `Expires: -1`; otherwise
`Cache-Control: private`). **Result: 66/66.** Every response (status, headers, body) is written to
`gate-responses.json` beside the test assembly, for a diff against the same requests on IIS.

**Not yet compared with IIS** (no Windows reference machine): the gate's oracle is Web API itself, so differences in
System.Web's own output are judged from the reference source, not observed on IIS. Known differences: an unmatched
route returns ASP.NET's 404 page where IIS serves its native 404 page (same status); IIS adds `Server: Microsoft-IIS/10.0`
and `X-Powered-By: ASP.NET` (here `Server: Kestrel`).

### Results (Release)

| Suite | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| System.Web.Http.WebHost.Test | 324 | 324 | 0 | 0 |
| System.Web.Http.Owin.Test | 243 | 243 | 0 | 0 |
| Microsoft.Owin.Tests | 321 | 321 | 0 | 0 |
| Microsoft.Owin.Host.SystemWeb.Tests | 36 | 36 | 0 | 0 |
| Microsoft.Owin.Security.Tests | 118 | 114 | 0 | 4 |
| WebApi.Gate.Tests | 66 | 66 | 0 | 0 |
| System.Web.MonoTests | 2,634 | 2,623 | 0 | 11 |
| System.Web.Http.Test | 3,157 | 3,155 | 0 | 2 |
| System.Net.Http.Formatting.Test | 6,445 | 6,445 | 0 | 0 |
| Razor / Cors / Http.Cors / Serialization | 1,460 / 72 / 124 / 9 | all | 0 | 0 |

First run of the new suites: 1,042 tests, 105 failures. TestApp (Release): all pages 200, 404 for an unknown page,
CSS served, after the System.Web changes.

### Phase 3 gate

- A stock Web API 2 sample returns the same JSON and status codes as Web API itself, on the fork's System.Web on Kestrel,
  through both WebHost and OWIN: **met** (66/66). Against IIS itself: **not verified** (no reference machine).

Open items: integrated-pipeline emulation; `Request.Headers` writes and other integrated-only APIs on the Kestrel host;
DPAPI payload compatibility; an IIS run of the gate requests; System.Web.Helpers (phase 5).

## Phase 4: MVC 5 without view compilation (30 Oct 2026)

### Lifted

| Project | Why now |
| --- | --- |
| System.Web.Mvc (5.3.1.0) | MVC |
| System.Web.WebPages, System.Web.WebPages.Razor, System.Web.WebPages.Deployment (3.0.0.0) | MVC references them (HTML helpers, anti-forgery, the Razor view engine types); `.cshtml` compilation is tested in phase 5 |
| System.Web.Mvc.Test | MVC's unit tests |

WebHost.Test's `HostedUrlHelperTest` (MVC URLs) is back in (19 tests). `Microsoft.TestCommon` now references the fork's
System.Web, so `Assert.ThrowsHttpException` (Microsoft builds it for .NET Framework only) is built on .NET too.
Microsoft.Web.Mvc ("MVC Futures") is not lifted: the MVC 5 template does not use it. (Lifted later the same day: see "MVC Futures" below.)

New lift-tool features (`tools/lift/lift_project.py`): `exclude`/`drop_refs`/`extra_refs`/`fork_refs` options,
`tools/lift/additions/`, `RE_TRANSFORMS` (regex), `MEMBERS_OUT` (compile out a member) and `MEMBER_EDITS` (skip a test,
or edit inside one member).

### Fixes in lifted code

| Where | Problem on .NET | Fix |
| --- | --- | --- |
| WebPages.Deployment `AppDomainHelper` | `GetIncompatibleDependencies` (tooling API) loads the bin assemblies in a new AppDomain | A collectible `AssemblyLoadContext` (unloaded afterwards), resolving from the bin directory |
| WebPages `CryptoUtil` (anti-forgery), MVC `OutputCacheAttribute` | `SHA256Cng` is Windows CNG | `SHA256.Create()`: the same hash |
| MVC Float/Double route constraints | Same shared source as Web API's: .NET's `TryParse` returns Infinity on overflow | The Web API fixups, applied to MVC too |
| MVC `LinqBinaryModelBinder`, Binary support in `Html.Hidden`, `HiddenInputTemplate`, `ModelBinders` | `System.Data.Linq` (LINQ to SQL) does not exist on .NET | Compiled out; **the public type `System.Web.Mvc.LinqBinaryModelBinder` is not built** (only a LINQ to SQL app would use it, and none can run on .NET) |
| MVC display/editor templates' `EntityState` check | `System.Data.EntityState` is in .NET Framework's System.Data.Entity.dll (EF 1-4); EF6's `EntityState` is another type that MVC never checked | Compiled out |

### Fork changes

| Change | File | Why |
| --- | --- | --- |
| Reason phrase | `AspNetCoreWorkerRequest.SendStatus` | IIS sends `Response.StatusDescription` (`HttpStatusCodeResult(418, "Custom description")`); Kestrel sent its own. Now set on `IHttpResponseFeature.ReasonPhrase` when it is not the standard one |
| CultureInfo in the BinaryFormatter port | `FrameworkSurrogates.cs` (`CultureInfoSurrogate`, `CultureMemberSurrogate`) | .NET's CultureInfo is not serializable; MVC's `ModelStateDictionary` carries one per `ValueProviderResult` (ModelState in TempData or session). Written with .NET Framework's fields in its order (`m_name`, `cultureID`, `m_useUserOverride`, `m_isReadOnly`; the lazily created members as null), rebuilt from the name on read; a stream's NumberFormatInfo/DateTimeFormatInfo/Calendar members are read and dropped. New test in Serialization.Tests |

### MVC tests corrected (platform differences and test infrastructure)

| Tests | Correction |
| --- | --- |
| 46 assertions (28 tests) | `(Parameter 'x')` message format (two regex transforms) |
| Newlines (25 tests, tests only) | `DefaultEditorTemplates.ObjectTemplate` writes `"\r\n"` on every platform, as on .NET Framework: its 7 tests use `"\r\n"`; select lists and the form validation script write the platform newline: 13 tests' CRLF verbatim strings get `.Replace("\r\n", Environment.NewLine)`; MVC's Razor parser tests use `"\r\n"` as System.Web.Razor.Test does |
| ThreadAbort (7 call sites) | `Thread.Abort`/`ResetAbort` are not supported; the tests throw a `ThreadAbortException` instance, as the fork's `Response.End` does (its message is the default "System error.") |
| Type names (10) | `mscorlib, Version=4.0.0.0, ...` is `typeof(int).Assembly.FullName` |
| BCL messages and frames | KeyNotFound and duplicate-key messages name the key; `Boolean.Parse` quotes the input; Activator's MissingMethodException message (it names the type, so MVC's `EnsureDebuggableException` keeps the original exception) and stack frames (`RuntimeType.ActivatorCache`, `CreateInstanceDefaultCtor`) |
| `ReflectedParameterDescriptorTest` (3) | .NET's `Attribute.GetCustomAttribute` casts a mock ParameterInfo's `object[]` to `Attribute[]` (.NET Framework used `as`); the mock answers the BindAttribute lookup with an `Attribute[]` |
| `TemplateHelperFormatsValuesUsingCurrentCulture` | ICU's es-MX time format (`4:12:08 p.m.`) differs from Windows NLS (`04:12:08 p. m.`); expected value built from the culture |
| `MvcTestHelper` | `AssemblyBuilderAccess.Save` → `PersistedAssemblyBuilder` (.NET 9+) |
| `TypeIsSerializable`, `DictionaryIsSerializable` | The fork's BinaryFormatter port, as System.Web uses |
| Skipped (2) | `FileExistsReturnsTrueForExistingPath_VPPRegistrationChanging`, `ViewContextGlobalValidationMessageElementAffectsLocalOne`: need a separate AppDomain |
| Compiled out (6) | LINQ to SQL `Binary` and EF 1 `EntityObject` tests (types that do not exist on .NET); `LinqBinaryModelBinderTest.cs` |

First run: 5,496 tests, 91 failures.

### Gate: the MVC 5 template

`test/webForCore.Mvc.Sample`: the Visual Studio "MVC" template's `RouteConfig` (with `IgnoreRoute`, plus
`MapMvcAttributeRoutes`), `FilterConfig` (global `HandleErrorAttribute`), `Global.asax` (`AreaRegistration.RegisterAllAreas`)
and `HomeController` (`View()` with `ViewBag.Message`), an Admin area, and controllers for model binding, validation,
filters, TempData and anti-forgery. Session state through the fork's ASP.NET Core session provider (TempData's default
`SessionStateTempDataProvider`). Views are not compiled yet: `PlainTextViewEngine` is added after the template's Razor
and Web Forms engines, which look for views and find none, and renders `View()` results as text.

`test/webForCore.Mvc.Gate.Tests` (29 tests) checks MVC 5.3's documented results:
- **Routes:** convention routes (with defaults, any case), attribute routes with constraints (`alpha`, `int:min(10)`), areas, `ActionName`; 404 from MVC's `HttpException` for missing actions, controllers, a verb mismatch (`[HttpPost]`) or a failed constraint.
- **Controllers and results:** `View()` with ViewBag and model, `JsonResult` with `AllowGet`, `ContentResult`, `HttpNotFound`, `HttpStatusCodeResult` with a description, redirects with `Location`, `X-AspNetMvc-Version: 5.3`.
- **Model binding and validation:** query-string arrays (repeated keys and `[0]`), optional parameters, a form with an indexed list and a nested object, DataAnnotations errors in ModelState order.
- **Filters:** a controller's `OnActionExecuting`, an `ActionFilterAttribute` on controller and action (the action's replaces the controller's: `AllowMultiple = false`), result filters changing the result, `[Authorize]` (401), `[ValidateAntiForgeryToken]` (round trip with `AntiForgery.GetTokens`, machine-key protection; missing and mismatched tokens rejected), an unhandled exception (ASP.NET's error page; `customErrors` is RemoteOnly and the request is local).
- **Post/redirect/get** with TempData across requests.

**Result: 29/29.** Not compared with IIS (no reference machine). Seen on the way and left as is: the fork's ASP.NET
Core session provider sets two cookies (`ASP.NET_SessionId` and `.AspNetCore.Session`); IIS sets only the first.

### Results (Release)

| Suite | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| System.Web.Mvc.Test | 5,498 | 5,496 | 0 | 2 |
| Mvc.Gate.Tests | 29 | 29 | 0 | 0 |
| System.Web.Http.WebHost.Test | 343 | 343 | 0 | 0 |
| webForCore.Serialization.Tests | 10 | 10 | 0 | 0 |
| All other suites (Web API, Katana, Mono System.Web, Formatting, Razor, Cors, Web API gate) | 14,676 | 14,659 | 0 | 17 |

TestApp (Release): all pages 200, 404 for an unknown page, CSS served.

### Phase 4 gate

- An MVC 5 template's routes, controllers, filters and model binding work: **met** (29/29 against MVC's documented
  behaviour, MVC's 5,498 unit tests pass). Against IIS: **not verified**.

Open items: Microsoft.Web.Mvc; views and `.cshtml` compilation, WebPages tests, System.Web.Helpers (phase 5); the session
provider's second cookie.

## MVC Futures: Microsoft's binary, then the lift (30 Oct 2026)

**Binary compatibility (handoff section 7).** Microsoft's original `Microsoft.AspNet.Mvc.Futures` 5.0.0 from nuget.org
(`Microsoft.Web.Mvc, Version=5.0.0.0`, unsigned, built for .NET Framework 4.0 against `System.Web.Mvc 5.0.0.0`,
referencing System.ServiceModel and System.Data.Linq) was referenced unmodified by the MVC sample and exercised through
`FuturesController`: `[AjaxOnly]` (an action method selector), `[ContentType]` (an action filter), `LinkBuilder`
(expression-based URLs through MVC's routing and expression helpers), `ModelCopier`, `[SkipBinding]`. All work on the
fork's MVC 5.3.1; .NET binds the 5.0.0.0 reference to it. `MvcSerializer` (`Html.Serialize`, `[Deserialize]`) fails with
a SerializationException: it needs NetDataContractSerializer, which .NET does not have. So a .NET Framework package built
against MVC runs on the fork as is, as long as it does not touch an API .NET lacks.

**Lift.** `src/webForCore.Microsoft.Web.Mvc` (Microsoft.Web.Mvc 5.3.1.0, unsigned as in Microsoft's build) and its tests;
the sample now uses the lifted build and passes the same tests.

| Fix | Why |
| --- | --- |
| `FileExtensionsAttribute`: names with .NET Framework's invalid path characters (`"`, `<`, `>`, `|`, NUL, 1-31) are invalid | .NET Framework's `Path.GetExtension` threw for them and Futures returned false; .NET's no longer checks |
| `MvcSerializer` throws PlatformNotSupportedException (wrapped in its SerializationException) | NetDataContractSerializer does not exist on .NET; the same result as Microsoft's binary |
| `XmlFormatHandler`: `DataContractSerializerSettings` | The 6-argument DataContractSerializer constructor does not exist on .NET; the same settings |
| Binary support in `BinaryDataModelBinderProvider` compiled out | System.Data.Linq |
| `System.ServiceModel.Syndication` package | Atom results (`SyndicationFeed`, `SyndicationItem`) |

Tests: 454 pass, 2 skipped (MvcSerializer round trips), 1 compiled out (LINQ to SQL Binary); messages corrected as for
MVC (parameter format, Activator's message and frames).

## Phase 5: views, Web Pages and System.Web.Helpers (30 Oct 2026)

### Fork changes: what Razor views needed

| Change | File | Why |
| --- | --- | --- |
| Configuration paths resolved case-insensitively | `UserMapPath.cs` (`ResolveIgnoreCase`) | Configuration paths are lower case by design (`machine/webroot/1/views/home/index.cshtml`). On Linux `/views` mapped to a directory that does not exist (it is `Views`), so the configuration system found no record for it and used the application's: **no web.config below the root was ever read** (`Views/web.config`, folder `<location>`-style settings, per-folder `authorization`, ...) |
| web.config found whatever its case | `WebConfigurationHost.GetStreamName` | The mapping fixes one base name per application (`VirtualDirectoryMapping` picks `Web.config` when the root has no `web.config`); `Views/web.config` was then looked for as `Views/Web.config` |
| .NET Framework's default compilation assemblies | `Config/machine.config` | .NET Framework's root web.config gives every page `System.Core`, `Microsoft.CSharp`, `System.Xml.Linq`, `System.ComponentModel.DataAnnotations`, ... The fork's list had none of them, so views using `dynamic` (ViewBag) or LINQ failed (CS1980). Added: the facades .NET ships (mscorlib, System, System.Core, System.Xml, System.Xml.Linq, System.Runtime.Serialization, System.ComponentModel.DataAnnotations, System.Data.DataSetExtensions) and what they forward to (Microsoft.CSharp, System.Linq, System.Linq.Expressions, System.Linq.Queryable, System.ObjectModel, System.ComponentModel.Annotations) |
| Requests for `.cshtml`, `.vbhtml`, `.cs`, `.master`, `.asax`, `.config`, ... go to System.Web | `AspNetCoreHost.ProhibitedExtensions` | The middleware answered 404 for them before System.Web saw them. The machine.config maps them as .NET Framework does (HttpForbiddenHandler: 403; Web Pages serves `.cshtml` when enabled), so they now behave as on IIS: `/Default.cshtml` is served by Web Pages, `/Web.config` and `/Global.asax` are 403. The middleware still blocks `.razor` and ASP.NET Core's `appsettings*.json` (404) |

Apps: the ASP.NET Core Web SDK compiles `.cshtml` files as ASP.NET Core Razor views at build time; for MVC 5 views it must
be off (`EnableDefaultRazorGenerateItems`, `RazorCompileOnBuild`, `RazorCompileOnPublish` false): the views are files
the fork compiles at run time, as on IIS.

### Lifted

System.Web.Helpers (3.0.0.0) and the tests of System.Web.WebPages, System.Web.WebPages.Razor,
System.Web.WebPages.Deployment and System.Web.Helpers.

| Fix in lifted code | Why |
| --- | --- |
| `WebGridDataSource`: unsortable values (mixed types, no IComparable) keep the original order | .NET's sort wraps the comparer's ArgumentException in an InvalidOperationException; .NET Framework's LINQ sort did not, and WebGrid catches ArgumentException |

Test infrastructure:
- **`AppDomainUtils.RunInSeparateAppDomain` on .NET** (`tools/lift/additions/Microsoft.TestCommon/AppDomainUtils.cs`): a
  collectible AssemblyLoadContext with fresh copies of the test assembly, Microsoft.TestCommon, System.Web and the Web
  Stack (static state isolated as in a new AppDomain), unloaded afterwards; the delegate is recreated there (captured
  strings, primitives and enums copied); AppDomain data is restored. 26 tests that Microsoft runs in separate AppDomains
  run this way; `WebUtils` (Microsoft's) is built on .NET too; `RuntimeEnvironment.IsVersion45Installed` is true.
- `app.config` of WebPages.Razor.Test as `testhost.dll.config` (ConfigurationManager reads the entry assembly's config).
- WebPages.Test: `PredefinedCulturesOnly` (see below); an aliased reference to System.Web.Mvc (HttpAntiForgeryException is
  TypeForwardedFrom System.Web.Mvc 2.0; on .NET Framework that came from the GAC); the four ScopeStorage test classes join
  Microsoft's "Uses ScopeStorage or ViewEngines.Engines" collection (they reset the static ScopeStorage; a test failed one
  run in two).

Accepted platform differences:

| Tests | Difference |
| --- | --- |
| WebImage (84), Chart (52): skipped | **System.Drawing (GDI+) is Windows only on .NET**: WebImage and Chart cannot draw on Linux (handoff section 9: SkiaSharp or ImageSharp). The fork's System.Web.DataVisualization also has no string resources (MissingManifestResourceException), a fork gap |
| Web Pages version negotiation (4): skipped | Side-by-side Web Pages versions (another System.Web.WebPages.Deployment, as from the GAC): no GAC, one version of a name per load context |
| `CodeDomDefinedExtensionThrowsTest`: skipped | .NET Framework registers a CodeDom provider for .js (JScript .NET) |
| `TryConvert_SkipsGrandfatheredTypes`: skipped | WindowsIdentity |
| `ConstructorWithMalformedVirtualPathSetsDefaultProperties`: skipped | `\` as a path separator |
| Legacy CAS (2), LINQ to SQL Binary (1): compiled out | Do not exist on .NET |
| Culture tests | ICU creates any well-formed culture name; .NET Framework threw CultureNotFoundException. **`<PredefinedCulturesOnly>true</PredefinedCulturesOnly>` restores that** (set in WebPages.Test and the samples; recommended for apps using `Culture="auto"`). ICU culture data: ar-DZ's decimal separator, en-US's U+202F before AM |
| Others | `CSharpCodeProvider` is the fork's (WebFormsForCore.Compilers); Microsoft.Web.Infrastructure is 2.0.0.0 (the current package); Windows environment variables (`Path`, `SystemDrive`); backslash paths in Deployment.Test input; parameter message format |

Results: WebPages.Test 971/973 (2 skipped), WebPages.Razor.Test 45/46 (1), WebPages.Deployment.Test 48/52 (4),
Helpers.Test 279/415 (136, GDI+). First run: 1,489 tests, 146 failures.

### Gate: layouts, partials and @helper

The MVC sample now has the template's Razor views (`_ViewStart`, `_Layout`, Home) and views for the rest. Mvc.Gate.Tests
(57 tests) adds:
- **ViewTests:** layout with child action (`Html.Action`) and section, inline `@helper`, App_Code helper, HTML encoding,
  `Html.Raw`, `Html.Partial` with a model, `Url.Action`, area links, a typed view with `DisplayFor` and a custom display
  template, a form with `EditorFor` (custom editor template), `LabelFor`, `ValidationMessageFor`, unobtrusive validation
  attributes and `AntiForgeryToken` (round trip), and that views and source are not served.
- **WebPagesTests:** `test/webForCore.WebPages.Sample`, a Web Pages site without MVC: `_AppStart` (AppState), `_PageStart`,
  layout, `RenderPage` with data, sections, `@helper`, `Request`, `Html.TextBox`, `Href`, `UrlData`, extensionless and
  `.cshtml` URLs, `_` pages blocked (404), `web.config`/`.cs`/`.csproj` 403, `appsettings*.json` 404.
- The MVC test classes share one sample process (an xunit collection fixture): processes running one app folder share its
  compilation output, and one compiling views made another restart (503).

**Result: 57/57.**

Known difference: requests for files under `/Views` get 403 (the classic `*.cshtml`/`*.config` mappings); IIS integrated
mode answers 404 through Views/web.config's `BlockViewHandler` (`<system.webServer>`, which the fork does not read).

### Results (Release, round 8)

| Suite | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| System.Web.WebPages.Test | 973 | 971 | 0 | 2 |
| System.Web.WebPages.Razor.Test | 46 | 45 | 0 | 1 |
| System.Web.WebPages.Deployment.Test | 52 | 48 | 0 | 4 |
| System.Web.Helpers.Test | 415 | 279 | 0 | 136 |
| Microsoft.Web.Mvc.Test | 456 | 454 | 0 | 2 |
| Mvc.Gate.Tests | 57 | 57 | 0 | 0 |
| All earlier suites (MVC, Web API, Katana, Mono System.Web, Formatting, Razor, Cors, Serialization, gates) | 20,527 | 20,508 | 0 | 19 |

TestApp (Release): all pages 200, 404 for an unknown page, CSS served, `Web.config`/`.cs`/`.Master` now 403 (were 404),
SessionTest postback persists.

### Phase 5 gate

- Layouts, partials and `@helper` render: **met** (with sections, child actions, App_Code helpers, display and editor
  templates and Web Pages sites). Against IIS: not verified.

Open items: GDI+ (WebImage, Chart); System.Web.DataVisualization resources; `<system.webServer>` (handlers, modules);
integrated-pipeline emulation; the session provider's second cookie; DPAPI payloads; an IIS reference run.
