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

## Hidden segments: the app's binaries were downloadable (30 Oct 2026)

Found while assessing the host layer. With `HandleAllRequestsWithWebForms`, every request reached System.Web, whose
static file handler served `/bin_dotnet/System.Web.dll` (any assembly of the app), `/obj/project.assets.json` and the
app's `*.deps.json` / `*.runtimeconfig.json` (200). IIS's request filtering hides `bin`, `App_Code`, `App_Data`,
`App_GlobalResources`, `App_LocalResources`, `App_WebReferences` and `App_Browsers` (404.8). `AspNetCoreHost` now
answers 404 for any path with one of those segments (ignoring case), the app's output folder (`bin_dotnet`) or `obj`,
and for `.deps.json` / `.runtimeconfig.json` files. `App_Data/*.config` was already 403 (`*.config`), now 404 as on IIS.
Gate: 6 more cases in Mvc.Gate.Tests (63/63). TestApp: pages, CSS and ScriptResource.axd unchanged.

## AppDomains and IIS on Kestrel (30 Oct 2026)

### PseudoAppDomain: in-process AppDomain emulation

.NET has no AppDomains. `System.Web.Hosting.PseudoAppDomain` (`AspNetCoreHosting/PseudoAppDomain.cs`) is a collectible
AssemblyLoadContext that stands in for the worker AppDomain:
- **What is per domain:** System.Web, the application's assemblies and everything else it deploys are loaded again into the domain, so all their static state is fresh. Also per domain: `Microsoft.CSharp` and the VB runtime, because their caches would otherwise hold every domain's types.
- **What is shared:** the shared framework (as the GAC was domain-neutral) and test frameworks.
- **How it resolves:** the deps.json resolution (TPA) first, then the app's output folder, then `bin`, then the app root.
- **Calls across the boundary:** the middleware and the domain exchange only shared-framework types (strings, arrays, delegates); there is no remoting. `AspNetCoreHost.CreateForMiddleware` builds the host inside the domain and returns `IsLegacyRequest`, `ProcessRequest` and `Shutdown` as delegates. Requests run under the domain's contextual reflection scope.
- **`ApplicationHost.CreateApplicationHost`:** creates its host in a new PseudoAppDomain. The result is the domain's own type, so callers use reflection or `dynamic`. The Windows-only check is now .NET Framework only.
- **Default for `UseWebForms`:** isolation is on (`IsolateApplication(false)` to opt out). A web.config, bin or App_Code change, `HttpRuntime.UnloadAppDomain` or a recycling policy shuts the domain down. The next domain starts right away (overlapped recycle) while the old one drains. Without isolation, a restart stops the process for the service manager to start it again; it used to spin in `ReleaseResourcesAndUnloadAppDomain`.
- **Recycling (`Recycling(...)`):** IIS's `periodicRestart` time (1740 minutes), requests, and private memory (KB).

Fork fixes the restart needed:
- `AspNetCoreHost.Stop` called `HttpRuntime.Close()`, which marked the runtime as shutting down, so the hosting environment's own shutdown never released the runtime or unloaded. It now only unregisters.
- `ProcessRequest` decrements its pending count in a `finally`.
- The in-process compiler loads `csc.dll` into the caller's context, not the default one.
- Domain creation runs on a clean ExecutionContext.

Releasing an unloading domain is what AppDomain.Unload used to do. These all run when the domain starts unloading, each action isolated:
- **Timers:** the domain's timers are disposed (by reflection over the runtime's timer queues). Shared-framework timers that captured the domain's ExecutionContext lose that context.
- **Event handlers:** handlers the domain's code added to process-wide events (AppContext and AssemblyLoadContext statics: `AssemblyLoad`, `AssemblyResolve`, `UnhandledException`, `ProcessExit`/`DomainUnload`, ...) are removed.
- **GC handles:** System.Web's strong GC handles (`GCUtil.Alloc`) are cleared.
- **Other references:** the `Resolving` handler is removed. The inotify watcher no longer captures a request's ExecutionContext. The domain's `ApplicationManager` context data lives in a static of its own copy, not in a table keyed by the domain.

**Old domains are collected** (fixed 31 Oct 2026, see "Old domains collected" below). This was open: SOS found no root, one of 9 old domains was collected, and the hosting layer stopped the process after `Recycling.MaxStuckDomains` stuck domains. That safety net stays.

Tests: the 9 AppDomain tests that were skipped now run:
- Mono's `SimpleWorkerRequestTest` (5), using `dynamic`. `SimpleWorkerRequest.MapPath` of a path outside the app throws `ArgumentNullException` again; .NET's `FileIOPermission` stub accepts null, so `InternalSecurityPermissions.PathDiscovery` now checks.
- Web API `ValidateSetsMemberNameProperty_*` (2) and MVC (2), through TestCommon's ALC-based `RunInSeparateAppDomain`. TestCommon's `SetAppData` also sets the load-context data the fork reads.

### IIS's modules from `<system.webServer>`

`WebServerConfiguration` (`AspNetCoreHosting/IIS/`) builds the effective section for a URL as IIS does:
- the applicationHost.config defaults (IIS 10), then the app's web.config, its `<location>`s and subdirectory web.configs;
- attributes override, and collections follow `add`/`remove`/`clear`;
- files are re-read when they change.

`IISModules` runs these in front of the application, on by default (`IIS(o => o.Enabled = false)` to opt out):

| IIS feature | Behaviour |
| --- | --- |
| serverRuntime `appConcurrentRequestLimit` (5000) | 503.2 |
| requestFiltering | fileExtensions 404.7 (IIS's default deny list: .cs, .config, .asax, ...), hiddenSegments 404.8 (plus the output folder and obj), verbs 404.6, double escaping and '+' 404.11, high-bit 404.12, denyUrlSequences 404.5, denyQueryStringSequences 404.18, maxAllowedContentLength 404.13, maxUrl 404.14, maxQueryString 404.15, headerLimits 404.10, alwaysAllowedUrls |
| httpProtocol | customHeaders on every response (IIS's default `X-Powered-By: ASP.NET`), redirectHeaders on 3xx, `removeServerHeader` |
| httpRedirect | destination with the rest of the URL below the configuring level, exactDestination, childOnly, wildcards, status Permanent/Found/Temporary/PermRedirect |
| URL Rewrite, inbound | `<rewrite><rules>` of every level (web.config files and `<location>`s), distributed as IIS does: a level's rules apply below it, matched against the URL relative to that level, parents' rules first; a relative rewrite or redirect URL is relative to the level, a '/'-rooted one to the site; isFile/isDirectory resolve under the level. ASP.NET Core's IIS rule parser compiles the rules, and `InboundRewrite` applies them one by one, so `stopProcessing` and redirects end the whole chain |
| URL Rewrite, outbound | `<outboundRules>` (`OutboundRewrite`, new: ASP.NET Core has none): `serverVariable="RESPONSE_x"` rewrites that header; otherwise the body, by `filterByTags` (A, Area, Base, Form, Frame, Head, IFrame, Img, Input, Link, Script, `<customTags>`) or, without it, every match. preConditions, conditions, negate, ECMAScript/Wildcard/ExactMatch, `{R:n}`/`{C:n}`, server variables, rewrite maps, `{ToLower:}`/`{UrlEncode:}`/`{UrlDecode:}`. A compressed body is not rewritten (IIS: 500.52) |
| security/ipSecurity | the first `<add>` that matches decides (ipAddress with a dotted mask or prefix length, or domainName with `enableReverseDns`), else `allowUnlisted`; `enableProxyMode` also checks X-Forwarded-For; denyAction Forbidden (403.6, 403.8 by name), Unauthorized, NotFound, AbortRequest |
| security/dynamicIpSecurity | `denyByConcurrentRequests` (403.501) and `denyByRequestRate` (403.502) per client address, `enableLoggingOnlyMode`, `enableProxyMode`, denyAction |
| directoryBrowse | `enabled="true"`: a directory without a default document is listed in IIS's format, with `showFlags` (Date, Time, Size, Extension, LongDate); hidden segments, the output folder and dot files are left out. Disabled, the request goes on to the application (as an extensionless URL does in integrated mode) |
| defaultDocument | IIS's list or the configured one, case-insensitive; 301 courtesy redirect of a directory URL without its trailing slash |
| staticContent | mimeMap over IIS's default map (ASP.NET Core's provider, derived from it); an unmapped static file is 404.3; clientCache (UseMaxAge, UseExpires, DisableCache, cacheControlCustom) |
| httpErrors | errorMode (DetailedLocalOnly by default), existingResponse Auto/Replace/PassThrough, error entries by status and substatus, File/ExecuteURL/Redirect. ASP.NET sets `TrySkipIisCustomErrors` on its error pages, so Auto passes them through, as on IIS |
| modules | entries with a managed type are added after system.web's modules; `<remove>`/`<clear>` apply to the merged list (integrated mode's single list) |
| handlers | managed entries are mapped before system.web/httpHandlers; TransferRequestHandler (integrated only) is skipped |
| security/authorization | IIS URL Authorization at AuthorizeRequest: any matching Deny denies, otherwise Allow, otherwise 401 (no 401.2 substatus: `SubStatusCode` is integrated only) |

`services.AddWebForms()` adds the parts ASP.NET Core has middleware for:
- **Compression:** urlCompression static on, dynamic off; httpCompression static/dynamic types and `minFileSizeForComp` (2700); gzip.
- **W3C logging:** IIS's fields, daily files. Differences: ASP.NET Core's field order, `cs-uri-query` with its `?`, and no substatus or win32 status.
- **systemd:** notify readiness, plus a watchdog in place of WAS's ping.

`tools/deploy/webforcore@.service` maps the app pool settings: rapid-fail protection, ping, shutdown limit, identity, memory and CPU limits.

Not done:
- Windows authentication (needs Kerberos and the Negotiate package);
- dynamic content caching (output caching in the kernel), `<serverVariables>` set by rewrite rules, the IIS 403.14 for a directory without a default document when browsing is off;
- configuration errors as 500.19 (a malformed web.config's section is ignored);
- `cs-username` in the W3C log.

Gate: `test/webForCore.IIS.Sample` and `test/webForCore.IIS.Gate.Tests`: one test or more per feature above, and the in-process restart (new static state, Application_Start again, every request answered during the restart).

### The worker request: what IIS gave ASP.NET (30 Oct 2026)

`AspNetCoreWorkerRequest` (the Kestrel side of `HttpWorkerRequest`):
- **WebSockets:** `HttpContext.AcceptWebSocketRequest` and `IsWebSocketRequest` work as with IIS 8's WebSocket module. The
  request runs through the pipeline; its response is held back; then ASP.NET Core makes the handshake (UseWebForms adds
  `UseWebSockets`) and the application's function runs with an `AspNetWebSocketContext` over Kestrel's WebSocket, which
  is aborted when the function returns, as on IIS. Options: `SubProtocol`, `RequireSameOrigin`. `WEBSOCKET_VERSION` is
  set, so Katana's System.Web host enables WebSockets (with a `FIXUPS` entry: Katana required IIS 8). Not done: aborting
  open sockets when the application shuts down (`AspNetWebSocketManager`).
- **HTTPS and client certificates:** `IsSecure` (Request.IsSecureConnection, the URL's scheme, SERVER_PORT_SECURE),
  `HTTPS`, `HTTPS_KEYSIZE`, and `Request.ClientCertificate` with `CERT_FLAGS` (present; issuer not trusted),
  `CERT_SUBJECT`/`CERT_ISSUER` in IIS's order (C=..., O=..., CN=...), `CERT_SERIALNUMBER` (IIS's dashed form),
  `CERT_COOKIE`, and the binary issuer, public key and validity. The certificate is the one Kestrel negotiated
  (`ClientCertificateMode.AllowCertificate` or `RequireCertificate`, IIS's "Accept" and "Require"); no renegotiation.
  Not available: the server certificate's `CERT_SERVER_*`/`HTTPS_SERVER_*`.
- **Server variables:** `INSTANCE_ID`, `INSTANCE_META_PATH`, `APPL_MD_PATH`, `GATEWAY_INTERFACE`, `REMOTE_PORT`,
  `SERVER_SOFTWARE` (Kestrel). As IIS 7's integrated mode does, a variable outside the classic list is asked of the
  worker request (`HttpServerVarsCollection`).
- **`IsClientConnected`:** false once the client has gone (`RequestAborted`) or the request timed out (was always true).
- **`ReadEntityBody(buffer, offset, size)`:** the offset was ignored, so `GetBufferlessInputStream` reads into the middle of
  a buffer were written at its start.
- **`executionTimeout`:** ASP.NET aborts the request's thread; .NET cannot. Before, a timed-out request hung for ever:
  `HttpContext.WaitForExceptionIfCancelled` waited for the abort. Now, when `RequestTimeoutManager` times a request out
  (its 15-second timer, not with `debug="true"`), the client gets ASP.NET's 500 "Request timed out.", `Request.TimedOutToken`
  is canceled, and at the next pipeline step the request ends with ASP.NET's `HttpException` ("Request timed out.",
  through `Application_Error` and the error events). The request's code runs on until it returns (nothing can stop it);
  what it writes is dropped, and the Kestrel request stays open until then so its HttpContext stays valid.

Gate (`ConnectionTests`): a WebSocket echo through `AcceptWebSocketRequest` (subprotocol, query string, close), the
handler without an upgrade, the HTTP and HTTPS server variables with a client certificate (the sample listens with TLS
too, `GATE_HTTPS_PORT`), a timed-out page (500 within the timer's period), a 200 KB bufferless read.

### SignalR 2, ASP.NET Identity 2, System.Web.Optimization (30 Oct 2026)

`tools/lift/lift_aspnet.py` lifts two more of Microsoft's open-source ASP.NET 4.x libraries the way the Web Stack was
lifted (files byte for byte, `FIXUPS` listed, same assembly names, delay signed with Microsoft's key):
- **SignalR 2.4.3** (SignalR/SignalR, Apache 2.0): `Microsoft.AspNet.SignalR.Core`, `.SystemWeb` and the .NET client
  (`.Client`, its netstandard2.0 build). Built as Microsoft built them: `SERVER`, `PERFCOUNTERS`, `NET45`, `SIGNED`, C# 7.3.
  Fixups: `AssemblyBuilder.DefineDynamicAssembly` (.NET has no `AppDomain.DefineDynamicAssembly`), and a `(string)null`
  where .NET 11's `ArraySegment<T>` made a null argument ambiguous. Not lifted: the Redis, SQL Server and Service Bus
  scale-outs, the JavaScript client (an app's own script), self-host.
- **ASP.NET Identity 2** (aspnet/AspNetIdentity, MIT): `Microsoft.AspNet.Identity.Core`, `.EntityFramework` (on
  EntityFramework 6.5.1, which runs on .NET), `.Owin`. No source changes.
- `Microsoft.Owin.Cors` (Katana; for SignalR's tests), through `lift_project.py`.
- System.Web.Optimization was already ported by WebFormsForCore (`src/WebFormsForCore.Web.Optimization`); the gate checks it.

Tests:
- **SignalR's unit tests** (`Microsoft.AspNet.SignalR.Tests` with `Tests.Common`, less IIS Express hosting and the Service
  Bus facts; the stress server's hubs compiled in): 487 of 489 pass, 2 skipped by SignalR. On Microsoft's xunit (2.4.0).
  Corrected: the check that a thread-pool thread keeps the default culture runs without the test's ExecutionContext
  (culture flows with it on .NET and .NET Framework 4.6+; BCL). `TieredCompilation=false`: two weak-reference tests rely on
  a local being dead after its last use, which .NET Framework's JIT gave and .NET's tier-0 code does not.
- **Identity's tests** (`Identity.Test`): 298 of 311 passed at first; all 311 pass with the DPAPI emulation (below). They need SQL Server: LocalDB is Windows-only, so
  `tools/lift/sqlserver.sh` runs SQL Server 2022 in a container (podman or docker) and the tests' App.config points at
  it (`FIXUPS`). On xunit 2 (Microsoft's were on xunit 1.9: `Assert.ThrowsDelegate` is `Action`, `Xunit.Extensions` is
  gone), one at a time as xunit 1.9 ran them (they share one database), in Pacific time (one test assumes UTC-8).
  Corrected: .NET's ArgumentException message format (BCL). The 13 that failed create Katana's
  `DpapiDataProtectionProvider` for token providers, which was Windows-only.
- **Gate:** a hub in the IIS sample (`App_Code/SignalRStartup.cs`, `OwinStartup`, `MapSignalR`) with SignalR's .NET client
  over WebSockets, Server-Sent Events and long polling, and `/signalr/hubs`; script and style bundles rendered, served
  concatenated and minified.

### System.Drawing on libgdiplus: tried and rejected (30 Oct 2026)

System.Drawing.Common 6.0 (the last version with a Unix implementation) over libgdiplus 6.2 (Fedora) on .NET 11, with
`System.Drawing.EnableUnixSupport`. Drawing, text, PNG/JPEG/GIF and resizing work, and 79 of the 137 WebImage and Chart
tests that ran passed. But **libgdiplus corrupts the heap and aborts the process** (`double free or corruption`) when a
24bpp or 32bppRgb bitmap that has been drawn on is rotated by 90 or 270 degrees. A JPEG loads as 24bpp, so WebImage's
resize-then-rotate takes down the whole server (2 tests). It reproduces in a 6-line program. A native crash can't be
contained by a PseudoAppDomain, and the library parses untrusted images. Not adopted. The other failures were the fork's
DataVisualization resources (19), Windows paths in the tests (9), missing Windows fonts (6) and save-path errors.
Next: a SkiaSharp-based System.Drawing, measured by the same 136 tests (below).

### System.Drawing on SkiaSharp (30 Oct 2026)

`src/webForCore.System.Drawing.Common` replaces the System.Drawing.Common package (Windows only since .NET 7) for the
whole fork (System.Web, System.Drawing, DataVisualization, AjaxControlToolkit and everything built on them). It is
built by `tools/lift/lift_drawing.py`:
- **Microsoft's code, unchanged except as listed:** System.Drawing.Common 6.0 (dotnet/runtime release/6.0), the
  csproj's own file list for a Unix build (199 files). That keeps the public API, argument validation, exceptions and
  .NET Framework-compatible behaviour of `Bitmap`, `Graphics`, `Image`, `Font`, `Icon`, ... The assembly keeps
  System.Drawing.Common's name and public key token, so references (and the shared framework's System.Drawing facade)
  resolve to it.
- **GDI+ in managed code on SkiaSharp** instead of libgdiplus: every `[DllImport("libgdiplus")]` (556 GDI+ flat API
  functions) becomes a call to `SkiaGdiPlus` (`tools/lift/additions/System.Drawing.Common/`), which implements 516 of
  them; the rest return GDI+'s NotImplemented status (generated stubs).
- **Lift fixups:** LibraryResolver no longer requires `System.Drawing.EnableUnixSupport` (it still loads libcups for
  printing); `ImageFormatConverter` throws NotSupportedException for an unknown name, as .NET Framework did (.NET Core
  3.0+ throws FormatException; Microsoft's System.Web.Helpers relies on the 4.8 behaviour).

The engine:
- **Images:** pixels as premultiplied BGRA (what Skia draws into), with the GDI+ pixel format kept apart and applied at
  the edges (LockBits, GetPixel, saving), so drawing never depends on it. GDI+'s reported formats (24bpp for JPEG,
  8bpp indexed for GIF, 32bppRgb for 32-bit BMP...), palettes, resolution, EXIF property items (read and written),
  GIF frames, delays and loop count, multi-page TIFF.
- **Codecs:** Skia decodes BMP, GIF, ICO, JPEG and PNG and encodes JPEG and PNG; webForCore's own code does what Skia
  cannot: BMP, GIF (halftone palette, LZW) and TIFF encoders, a baseline TIFF reader (none/LZW/PackBits; grey, palette,
  RGB, RGBA), EXIF and the GDI+ codec list. Formats GDI+ cannot read (WebP...) are rejected as on Windows.
- **Graphics:** an SKCanvas with GDI+'s state: world, page (unit and scale) and container transforms, device-space
  clipping, Save/Restore, smoothing, compositing and interpolation modes, and GDI+'s pixel-centre convention
  (PixelOffsetMode None puts pixel centres on integer coordinates; Skia's are at +0.5). DrawImage at physical size
  (resolution), source units, parallelograms, ImageAttributes.
- **Brushes, pens, paths, regions:** solid, hatch (8x8 patterns, the common styles exact), linear gradients
  (blends, preset colours, sigma/linear shapes, wrap modes, transforms), path gradients (drawn as GDI+ defines them,
  centre to boundary colours, as a vertex mesh), textures; pen widths, units, dash styles and caps, joins, anchor caps;
  GDI+ path data (arcs, ellipses and cardinal curves as GDI+'s Béziers), flatten, widen, warp, iterators; regions on
  Skia path operations.
- **Text:** families resolve through fontconfig; the Windows core fonts map to installed metric-compatible ones
  (Arial, Times New Roman, Courier New to Liberation; Calibri, Cambria to Carlito, Caladea) and keep their names, as
  libgdiplus did; GDI+-style layout: 1/6 em padding (not for GenericTypographic), word wrap, alignment, trimming,
  hotkey prefixes, character ranges, AddString.
- **ImageAttributes:** remap tables, colour keys, colour matrices (with grey modes), threshold, gamma, output channel,
  per adjust type, applied exactly on the CPU.

Not implemented: metafiles (EMF/WMF), X11/macOS surfaces,
SaveAdd (multi-frame saving), the encoder parameter list, pen compound arrays and alignment, path-gradient blends and
focus scales, ICC profiles, JPEG-compressed or tiled TIFF. Text: no font fallback for characters a font lacks, no
bidirectional or vertical layout, tabs as four spaces.

Also: `src/WebFormsForCore.Web.DataVisualization/Common/SR.resx`, the chart strings, from dotnet/winforms-datavisualization
(MIT; 1,288 of the 1,290 keys, the others are Windows Forms accessibility switches). Without them every Chart threw
MissingManifestResourceException.

Tests: System.Web.Helpers.Test's 136 WebImage and Chart tests run (they were skipped). WebImageTest's Windows paths
(`x:\file.jpg`, rooted on Windows only) become Linux-rooted, and ChartTest strips a drive letter only if there is one
(test data, `RE_TRANSFORMS`). **Result: 418/418** (was 279 passed, 136 skipped). With libgdiplus: 79 of 137 passed and
the process crashed.

### System.Drawing.Common's own tests (30 Oct 2026)

`tools/lift/lift_drawing_tests.py` lifts Microsoft's System.Drawing.Common 6.0 test suite (90 files) with what it needs:
TestUtilities (PlatformDetection, AssertExtensions) from dotnet/runtime, Microsoft.DotNet.XUnitExtensions and
RemoteExecutor from dotnet/arcade release/6.0 (built from source instead of the dotnet-eng feed), and the test data
from dotnet/runtime-assets release/6.0. Test files are unchanged. The suite's "is GDI+ available" checks probe for
libgdiplus and screen out old libgdiplus builds (by version, and on RedHat/Fedora/openSUSE); those four checks answer
for SkiaGdiPlus instead (fixups in the script). Selection is upstream's (`category!=failing` on Linux).

Result (tools/lift/test-results, round 11): **3,716 of 3,883 pass** in Microsoft's Linux selection; of the 151
failures, 141 are metafiles, 8 are deliberate differences from libgdiplus or .NET Core (listed there), 1 compares
against Windows' pixels and 1 is premultiplied-alpha rounding. Of the 1,008 tests Microsoft disabled on Unix because
libgdiplus failed them, 560 pass. The first run had 563 failures in the Linux selection.

Engine changes the suite drove, each following Windows GDI+ as the tests record it:
- **Lift:** `static extern unsafe` imports were not rewritten (GdipGetMatrixElements(float*) still called libgdiplus).
  Every Graphics function now returns ObjectBusy while the Graphics' HDC is out (generated in the lift).
- **Pixels:** the GDI+-to-Skia shift is 0.5 - 1/32, not 0.5: Skia's aliased rasterizer rounds an edge on a pixel
  centre the other way from GDI+, which put fills, clips and even-width pens one pixel off. Aliased 1 px lines cover
  both end pixels.
- **Matrix:** IsIdentity within 0.0001; finite overflow saturates at float.MaxValue (infinities stay); non-finite
  matrices are not invertible; invalid MatrixOrder is rejected (on brushes and pens Multiply treats it as Append and
  rejects non-invertible matrices); world transforms stay invertible.
- **LinearGradientBrush:** GDI+'s model, a horizontal gradient across the rectangle mapped by the brush transform; the
  constructor's angle is in Transform (rotation about the centre, scaled to span the corners).
- **Graphics:** BeginContainer resets the settings and clip (the outer clip becomes the container's), folds the page
  transform into the container and rejects World/Display units; DrawImage rejects World/Display source units and
  overflows past GDI+'s 28.4 fixed-point range.
- **Handles:** HDC, HICON, HBITMAP and HRGN are handles (libgdiplus's convention, which System.Drawing's Unix code
  assumes); region data is written and read in GDI+'s format (one node per region; any node tree is read).
- **Images:** ImageFlags as GDI+ (memory bitmaps HasAlpha only; HasRealDpi only when the file states a resolution;
  JPEG's colour space), icons decode the image nearest 16x16, PNG/TIFF write alpha unless the format has none
  (32bppRgb keeps it), default palette flags, missing files are ArgumentException.
- **Validation and details:** custom line caps, pen caps and dash patterns, path reverse markers and warp, region
  normalisation and half-open containment, MeasureCharacterRanges ranges and negative layouts, ImageAttributes flags,
  path-gradient bounds, surround colours and 256/511-point sigma blends, ToLogFont's charset, codec masks.

## Protected configuration, the integrated pipeline and precompiled sites (30 Oct 2026)

### DPAPI and protected configuration

`WebFormsForCore.Security.LocalDataProtection` (in System.Configuration; `lift_project.py` copies it into Katana's
Microsoft.Owin.Security through the new `fork_sources` setting) is DPAPI where Windows has it (`ProtectedData`) and its
emulation elsewhere: one master key per scope, as DPAPI keeps one per machine and per user, in a file readable by its
owner only. Keys live in `$WEBFORCORE_KEYS` if set; otherwise machine-scope keys go in `/var/lib/webforcore/keys` when
it is writable, and everything else goes in `~/.webforcore/keys`. A key is created the first time it is needed.
Payloads are AES-256-CBC with an HMAC-SHA256, keyed by HKDF from the master key, a salt and the entropy. As with DPAPI,
the wrong key or the wrong entropy fails with "Key not valid for use in specified state."
- **`DataProtectionConfigurationProvider`** (DPAPI) encrypts and decrypts with the emulation. A section encrypted by
  Windows DPAPI is recognised (its blob header) and fails with an explanation: those keys never leave the machine.
  Decrypt it there, and encrypt it again here or with RSA.
- **`RsaProtectedConfigurationProvider`**: CAPI key containers are files, `<container>.rsakey.xml`, in the machine or
  user key directory. The file holds the RSA key as XML; a bare `RSAKeyValue`, as `aspnet_regiis -px` exports it, is
  accepted, so a key exported from Windows can be imported. Keys are created on first use (2048-bit) unless the key must
  already exist, exportability is kept, and Add, Delete, Import and Export are supported.
- **`webforcore-regiis`** (`src/WebFormsForCore.RegIIS`) is aspnet_regiis's configuration part: `-pef`/`-pdf` (encrypt or
  decrypt a section of a site's web.config), `-pc` (create a container, `-exp` exportable, `-size`), `-pz` (delete one),
  `-px` (export, `-pri` with the private key), `-pi` (import), `-prov`, and `-pku` (user scope). No IIS metabase:
  `-pe`/`-pd` with `-app` are not supported.
- Fork fix: in a PseudoAppDomain, `<configProtectedData>` failed with "does not inherit from
  IConfigurationSectionHandler". `BaseConfigurationRecord` named the section types by their .NET Framework assembly
  name, which resolves to the shared framework's System.Configuration, a different type. It now takes the names from
  `typeof`.
- Katana's `DpapiDataProtectionProvider` uses the emulation in user scope. With it, Identity's tests pass 311 of 311.

Gate (`ProtectedConfigurationTests`): the sample's `protected/web.config` has an RSA-encrypted `<connectionStrings>` and
a DPAPI-encrypted `<appSettings>`, read by a page. The sample's test keys are in `App_Data/keys`, which is not served.
A section encrypted by Windows DPAPI gives the explanation. regiis round trip: encrypt with key directory A, fail to
decrypt with B, export from A, import into B, then decrypt with B. A non-exportable key's export is refused.

### Integrated-pipeline APIs in classic mode

The fork runs ASP.NET's classic pipeline. The APIs that throw "This operation requires IIS integrated pipeline mode"
on IIS classic mode now work on Kestrel instead, as they do in integrated mode:
- **Notifications:** `HttpContext.CurrentNotification` and `IsPostNotification` report the step that is running. The
  classic pipeline gains `MapRequestHandler`, `LogRequest` and `PostLogRequest` (raised in integrated mode's order, the
  log events after the handler and before `EndRequest`). `Response.AddOnSendingHeaders` callbacks run when the headers
  are written, with `CurrentNotification` set to `SendResponse`.
- **Request changes:** `Request.Headers` and `Request.ServerVariables` can be set and removed. HTTP_ variables follow
  the headers, and headers changed in a module are what later modules and the handler see. `Request.InsertEntityBody`
  replaces the request body; `Request.Abort` aborts the connection.
- **`Response.SubStatusCode`** sets the substatus `<httpErrors>` matches, so it picks the custom page for it (for
  example 404.7). The W3C log does not have `sc-substatus`.
- **`Server.TransferRequest`**: the current request ends, and a new request for the new URL runs through the whole
  pipeline on the same Kestrel connection, with the method, headers, body and user, and with the changes the overloads
  allow. A transfer chain deeper than 16 fails (a guard against loops).

Gate (`IntegratedPipelineTests`), with a module in the sample (`App_Code/IntegratedModule.cs`) and pages in
`integrated/`: notifications in order (including the log events), headers and server variables changed by the module,
`AddOnSendingHeaders`, `TransferRequest` (to a new URL, and a POST with its form kept), a substatus that selects
`<httpErrors>`' custom 404.7 page, and `Abort`.

### Precompiled sites (aspnet_compiler)

`src/WebFormsForCore.AspNetCompiler` (`aspnetcore_compiler`, aspnet_compiler's switches) now builds against the fork's
projects instead of WebFormsForCore 1.7.0-rc3's packages. Two fork fixes made it precompile for .NET:
- `AssemblyBuilder.FixUpCompilerParameters`: in the precompiler, the compiler's core library was the runtime's
  `mscorlib.dll`, which only forwards to `System.Private.CoreLib`. Every compilation then failed ("Predefined type
  'System.Void' is not defined"). When the target is the running runtime, the core library is `System.Private.CoreLib`.
- `BuildManager.SetPrecompilationInfo`: the fork's BuildManagerHost calls it a second time, which found the marker file
  the first call had just written and refused the target ("The target directory is not empty") unless `-f` was given.
  The second call is now a no-op for the same target.

The precompiled output goes into the folder that `-b` names. A deployment uses the host's output folder (`-b bin_dotnet`)
and puts the host's build there too. The app then runs as usual (`dotnet bin_dotnet/<app>.dll` from the site folder).

| Precompilation | What the target holds | On the fork |
|---|---|---|
| Fixed (no `-u`) | marker files for pages, `.compiled` maps, `App_Code.dll`, `App_global.asax.dll`, `App_GlobalResources.dll`, `App_Web_*.dll` | pages served from the precompiled assemblies; a page added later gets IIS's 500 "has not been pre-compiled, and cannot be requested" |
| Updatable (`-u`) | pages' markup with `CodeFile` replaced by `inherits="Class, App_Web_x"`; no code files | markup compiled at run time against the precompiled code-behind; edits and new pages take effect |

A compile error fails the precompilation (exit code 255; Windows' tool returns 1). The error is reported as
aspnet_compiler reports it, `file(line): error CSxxxx: message`, with the physical path.

Gate (`PrecompilationTests`), on `test/webForCore.Precompiled.Site` (CodeFile code-behind, a master page, a user control,
App_Code, Global.asax, App_GlobalResources) and a minimal host, `test/webForCore.Precompiled.Host`. Fixed: the target's
contents, the rendered page, and a page added later refused. Updatable: the markup rewritten, edits picked up, a new
page compiled. A broken page fails the precompilation with its error.

## Documentation, CI and licences (30 Oct 2026)

- **Docs** in `docs/`: MIGRATING, AGENT-MIGRATION (instructions for an AI agent), NAMESPACES, IIS-COMPATIBILITY,
  TESTING (with coverage), MODIFICATIONS, and `lifted-changes.md`, which `tools/lift/changes_report.py` generates. It
  diffs every lifted file against `git show <commit>:<path>` in the upstream checkouts (4,389 files, 196 changed),
  then the fork's changes to WebFormsForCore's port since `52f4172`.
- **AjaxControlToolkit vendored.** The submodule was pinned to a local commit (`92be3f03`, "Build on Linux and target
  net11.0 only") that no remote has, so a fresh clone couldn't check it out. Its 1,565 tracked files are now in this
  repository (licence BSD-3-Clause, CodePlex Foundation). The stale `.gitmodules` entries (referencesource, dotnet,
  mono, SoapFormatter, CassiniDev, AspNetWebOptimization, WebGrease, Redesigner) are removed; their origins are in
  NOTICE. BundleTransformer and `www` stay submodules, and the build doesn't use them.
- **A clean build needs `src/WebFormsForCore.Build` first.** Fifteen projects import
  `lib/WebFormsForCore.Build/netstandard2.0/FakeStrongName.targets`, which is that project's output and is ignored by
  git.
- **CI** (`.github/workflows/ci.yml`, Ubuntu 24.04): builds the build tasks, then the solution, fetches the
  System.Drawing test fonts (`tools/ci/fetch-test-fonts.sh`), and runs `tools/ci/run-tests.sh` with SQL Server as a
  service container. System.Drawing's 151 known failures are in `tools/ci/known-failures/`;
  `tools/ci/check_failures.py` fails on any other.
- **Licences:**
  - `LICENSE` states how the repository is licensed, and keeps upstream's copyright line.
  - `LICENSE-BSD-3-CLAUSE.txt` covers AjaxControlToolkit and Redesigner.
  - `NOTICE` lists every component. It adds the Reference Source, AjaxControlToolkit, Redesigner, Web Optimization,
    WebGrease, RoslynCodeDomProvider, the BinaryFormatter and SoapFormatter sources, the Roslyn binaries and the
    JavaScript libraries. It also flags material inherited from WebFormsForCore that no open licence covers:
    `webengine*.dll`, the decompiled Report Viewer, and other decompiled sources.
  - Package metadata now follows each component's licence: lifted Web Stack, Katana and SignalR are Apache-2.0,
    Identity and System.Drawing.Common are MIT, Web Optimization and WebGrease are Apache-2.0, AjaxControlToolkit is
    BSD-3-Clause. The copyright and authors fields replace "Estrellas de Esperanza", and the project URL is this
    repository.

## Licence cleanup: inherited non-open material (30 Oct 2026)

`docs/LICENSING.md` lists what WebFormsForCore's fork base contained that no open licence covers. Done:
- **webengine.dll / webengine4.dll removed** (.NET Framework native binaries, packed for `runtimes/win-*`), with the
  empty `WebFormsForCore.NativeDlls` project. `UnsafeNativeMethods.GetHMACSHA1Hash`/`GetSHA1Hash` use the managed code
  on every OS (they called webengine4 on Windows).
- **Microsoft.Web.Infrastructure rewritten.** The 27 decompiled files went: the csproj's `Compile Remove` named a folder
  that doesn't exist, so the `DynamicValidationHelper` copy compiled too. The documented API is now four files:
  - `DynamicModuleUtility.RegisterModule` → `HttpApplication.RegisterModule`;
  - `HttpContextHelper.ExecuteInNullContext`;
  - `InfrastructureHelper.UnloadAppDomain` / `IsCodeDomDefinedExtension`;
  - `ValidationUtility`, which forwards to System.Web's `DynamicValidationShim`, the hook System.Web keeps for this
    assembly to find by reflection.

  The Windows-registry "kill bit" and the integrated-pipeline module shim are gone. The assembly name, version
  (2.0.0.0) and key are kept.
- **System.Web.RegularExpressions:** the decompiled classic regex classes (`Regex.CompileToAssembly` output) compile for
  .NET Framework only. System.Web on .NET already used `NetCoreRegexes`; one designer-only `typeof(TagRegex35)`
  check in `BaseParser` is `#if NETFRAMEWORK`.
- **`DataProtector` rewritten** from the documented API. The decompiled class was not abstract, and its `Create`
  ignored `providerClass`, so `<machineKey dataProtectorType>` always used DPAPI through `ProtectedDataPortable`. That
  is Mono's managed DPAPI, which needs CAPI (`CspParameters`) and threw on Linux. Now `Create` resolves the type
  (CryptoConfig, then `BuildManager.GetType`), and `DpapiDataProtector` uses `LocalDataProtection`.
- **Microsoft Report Viewer removed** (`src/WebFormsForCore.Web.Reporting`, decompiled, never built).
- **System.Web.Mobile** stopped building in the full solution after the System.Drawing lift: `FontNameConverter` used
  `SystemEvents`, which came with the NuGet System.Drawing.Common. The test suites only build their dependencies, so
  this went unnoticed. It also threw `PlatformNotSupportedException` on Linux when created, and `FontInfo.Name` uses
  it at run time. The `InstalledFontsChanged` subscription was dropped instead of adding the Windows-only package; the
  font list is read once per converter. Checked on Linux: `FontInfo.Name`'s converter turns "arial" into "Arial".

Open: about 140 decompiled System.Configuration files. 125 have their originals in the Reference Source and the
configuration builders in dotnet/runtime. A trial replacement (an isolated copy, 128 files out, 93 Reference Source
files in) built with 113 errors:
- 47 accessibility mismatches (`protected` vs `protected internal` overrides);
- 28 duplicate types (`TypeUtil`, System.dll internals in `Internal.cs`);
- 10 unsafe blocks;
- about 20 missing types.

To be done as a lift script with fixups.

## Licence cleanup, part 2: no decompiled code left (30 Oct 2026)

Every source file in the port without a licence header was compared line by line with the Reference Source. Matching
files are Microsoft's MIT code with the header missing; the rest were traced. All decompiled code and API
reconstructions are now replaced (details and table: `docs/LICENSING.md`):
- **System.Configuration** (`tools/lift/lift_configuration.py`): 152 decompiled files out, 89 Reference Source files
  in, plus the configuration builders and `WindowsAuthenticationElement` (no MIT source exists). Fixups:
  - System.dll's section classes override protected internal members, since they now share an assembly;
  - System.Net's `*SectionInternal` plumbing is left out (.NET's System.Net doesn't read `<system.net>`);
  - default module type names as strings; no ClickOnce and no CAS grant sets;
  - `SettingsPropertyValue` serializes with the BinaryFormatter port;
  - 29 messages from System.dll that were empty now have their text from the Reference Source's `System.txt`;
  - `SR` is generated from the `.resx` (`tools/lift/gen_sr.py`).
- **System.Web.ApplicationServices** (`lift_appservices.py`): the Reference Source. The custom loader runs in the
  current domain (no `AppDomain.CreateDomain`); `MembershipAdapter` is found by System.Web's simple name.
- **aspnet_compiler** (`lift_aspnet_compiler.py`): the Reference Source's `aspnet_compiler.cs` and `CompilerResources`,
  with WebFormsForCore's additions (`-b`, `-t`, precompilation in its own load context, error events) in
  `Precompiler.WebFormsForCore.cs`. Running the tool without `-nologo` used to fail because the resource name didn't
  match; a gate test now checks the usage text (IIS gate: 58).
- **ResX** (`lift_resx.py`): dotnet/winforms release/8.0. winforms' `BinaryFormat` fast paths are left out (the
  formatter writes the same format), and the BinaryFormatter port does the serializing. Its strings come from
  `ResXSR`, so it never collides with System.Web's `SR`.
- **StronglyTypedResourceBuilder** from Mono and **`BindableTypeAttribute`** from the Reference Source (`lift_strb.py`).
- **Resource accessors** generated by `gen_sr.py` from each project's `.resx`: System.Web's `SR` (3,369 names),
  System.Web.Services' `Res`, and System.Web.Mobile's two `SR` classes. The Mobile one is non-static, because the
  Reference Source subclasses it.
- **Written from the documented APIs:** `CallContext`, `AppDomainManager`, `TransactionOption`, `UrlBuilder`, and the
  `AssemblyRef`/`ThisAssembly` identity constants. System.Web's `AssemblyRef` also serves System.Web.Extensions and
  DataVisualization through `InternalsVisibleTo`.
- **Removed:**
  - the decompiled System.Web.RegularExpressions classes, which were never compiled for .NET;
  - five `GlobalSuppressions` files with no original;
  - unused copies of .NET Framework's config files, and Microsoft's descriptive comment in `machine.config`;
  - the TestApp's extracted `machine.config`;
  - the BundleTransformer and `www` submodules, which the build doesn't use.
- Four System.Web.Extensions files and one Mobile file imported `System.Windows.Forms`, which existed only because of
  a decompiled class. The imports were removed (Extensions) or put under `#if NETFRAMEWORK` (Mobile).
- **Resource names checked.** Two Reference Source resource classes look up their strings by a fixed name (aspnet_compiler's
  `CompilerResources`, ApplicationServices' `ApplicationServicesStrings`), and so does `ResXSR`. Each needed a
  LogicalName in its project; until then, any error message from them failed to load. Every `new ResourceManager("...")`
  in the port is now checked against the resources embedded in the built assembly. Two names match nothing, and neither
  is ever read: `Borders3D.ResourceManager` (DataVisualization, `System.Web.UI.DataVisualization.Charting`) has no
  caller, and the HtmlEditor `Popup`'s `_rm` (AjaxControlToolkit, `ScriptResources.BaseScriptsResources`) is assigned
  and never used. Not defects; left as they are.

## MVC Futures: Html.Serialize and [Deserialize] work (30 Oct 2026)

`MvcSerializer` serializes with `NetDataContractSerializer` (binary XML), then signs and encrypts with the machine key.
`NetDataContractSerializer` is .NET Framework only, so the lift had replaced both calls with a throw, and two
`MvcSerializerTest` tests were skipped. Now `tools/lift/additions/Microsoft.Web.Mvc/NetDataContractSerializer.cs`
provides an internal class of that name, which `MvcSerializer` finds by its simple name. Both fixups and both skips are
gone, and `MvcSerializer.cs` is byte for byte upstream.

How the replacement works: `DataContractSerializer` with a `DataContractResolver`.
- The serializer's own contracts (XML Schema primitives and the `schemas.microsoft.com/2003/10/Serialization`
  namespaces) keep their names; every other type is written as its full name, with its assembly as the namespace.
  That is what made `NetDataContractSerializer` read a graph back without known types.
- On reading, a URI namespace goes to the built-in resolver, and an assembly name is loaded. Delegates are refused
  either way, although the payload is machine-key protected before it's read.
- `PreserveObjectReferences` is on, as with `NetDataContractSerializer`.
- The format differs from .NET Framework's, which only matters for a value serialized there and read here.

Tests: Microsoft.Web.Mvc.Test 456/456 (2 more run). The MVC gate (64) has a wizard: `Html.Serialize` of a
`[Serializable]` graph (list, date, nested object) in a form, read back by `[Deserialize]` on the post. A tampered
value fails the request with MvcSerializer's "Deserialization failed." (500), as on .NET Framework.

## ASMX, ASP.NET AJAX, mail and provider registration (30 Oct 2026)

New IIS gate suites for features with no tests until now, and the fixes they needed.

**ASMX** (`WebServiceTests`, 7 tests, `services/Catalog.asmx` with `App_Code/Catalog.cs`): `?WSDL`, SOAP 1.1, SOAP 1.2
with a complex return type, an exception as a SOAP fault, HTTP POST from the local machine, the help page, and
`[ScriptService]` JSON. Three fixes:
- **CS0012 compiling App_Code** (`MarshalByValueComponent` "defined in an assembly that is not referenced"): .NET's
  `System.dll` facade forwards types to `System.ComponentModel.TypeConverter` and others, which the run-time compiler
  wasn't given. `AssemblyBuilder` now adds the targets of the forwarded types of each runtime-directory reference.
- **CS0433 `WsiProfiles`**: the WCF client package puts `System.Web.Services.Description.dll` in `bin`, which defines
  types System.Web.Services also has. `CompilationSection` skips that assembly when it adds `bin` assemblies.
- **"A non-collectible assembly may not reference a collectible assembly"**: `XmlSerializer.FromMappings(mappings)`
  generates its serializer in a non-collectible context, but the service type lives in the app's collectible
  PseudoAppDomain. `SoapServerProtocol`, `SoapServerMethod` and `XmlReturnReader` pass the service type
  (`FromMappings(mappings, type)`), which puts the generated assembly in that type's context.

**The ASMX help page.** A browser opening a `.asmx` got an empty page: nothing configured a help generator.
.NET Framework's root web.config names `DefaultWsdlHelpGenerator.aspx` in its CONFIG folder, a Microsoft file that
isn't open source. webForCore has its own page (`src/WebFormsForCore.Web.Services/DefaultWsdlHelpGenerator.aspx`, MIT),
embedded in System.Web.Services; machine.config names it, and `DocumentationServerProtocol` extracts it next to the
extracted machine.config before compiling it. It lists the operations, links the WSDL and, for local requests, has an
HTTP POST test form. No SOAP samples or conformance warnings.

**ASP.NET AJAX** (`AjaxTests`, 4 tests, `ajax/Panel.aspx`): the ScriptManager's scripts from `ScriptResource.axd`, an
UpdatePanel partial postback (the delta response, only the panel rendered), a full postback of the same page, and a
page method called with JSON. No fixes needed. Two things the tests had to respect, both as on IIS: partial
rendering depends on the browser definitions, so a client without a known User-Agent gets none; and the sample's
`maxQueryString="100"` (a request filtering test) rejects `ScriptResource.axd` URLs, so the sample lifts it there.

**`System.Web.Mail.SmtpMail.Send`** threw `PlatformNotSupportedException` (it drove Windows CDO). It now maps the
message to `System.Net.Mail` (`src/WebFormsForCore.Web/WebFormsForCore/SmtpMailSender.cs`): addresses (`;` or `,`
separated), subject, body and format, encoding, priority, headers, `Content-Base`/`Content-Location`, attachments,
and the CDO fields for server, port, SSL, timeout, basic authentication and pickup directory. machine.config's
`<system.net><mailSettings>` declarations now name `System.Configuration.ConfigurationManager`, where the types are, so
the section is readable. `MailTests` sends to a pickup directory and checks the message file.

**`<system.data><DbProviderFactories>`** entries were ignored (.NET's `DbProviderFactories` has no configuration).
`DbProviderFactoriesSectionHandler` (`src/WebFormsForCore.Web/WebFormsForCore/`) parses `<add>`, `<remove>` and
`<clear>`, inheriting the parent's list, and `HttpRuntime` applies the result with `RegisterFactory` and
`UnregisterFactory` at application start. machine.config's `system.data` section uses it. `DataTests` checks an added
provider, a removed built-in and a kept one.

The full regression (round 16) caught one regression from the CS0012 fix: `netstandard.dll` forwards `HttpUtility` to
.NET's `System.Web.HttpUtility.dll`, and referencing it as well made `HttpUtility` ambiguous with System.Web's
(CS0433 on any page using it). Forward targets whose types System.Web defines are now left out. Round 16: 27,290
tests in 24 suites; 27,104 pass, 35 skipped, 151 known System.Drawing failures. IIS gate 71 (13 new).

## Old domains collected (31 Oct 2026)

Old PseudoAppDomains were never collected after a restart (the "open issue" above). Method: a harness restarts a
sample N times under a workload (the IIS sample's pages, ASMX, AJAX, mail, data; the MVC, Web API and Web Pages
samples' gate URLs), waits for the middleware's unload check, and takes a heap dump; `gcroot` of each old domain's
`LoaderAllocator`, ignoring handle type 10 (.NET 9+'s weak interior-pointer handles, which SOS lists as roots); where
SOS showed nothing, bisecting the workload. Ten holders, each released when the domain unloads
(`AspNetCoreHosting/PseudoAppDomain.cs`, `ReleaseProcessResources`):

| Holder | Why | Release |
| --- | --- | --- |
| `PseudoAppDomain`'s own fields | Once unloading starts, the runtime holds the context with a strong handle until its LoaderAllocator is collected (`AssemblyLoadContext.InitiateUnload`). A field referencing the domain's System.Web assembly (and failure exceptions, whose stack traces keep their methods) closed a cycle through that handle. This was the holder SOS could not explain. | The assembly is held weakly; failures are kept as text |
| `AppDomain.DomainUnload` | An instance field of the process's AppDomain on .NET (never raised); `HostingEnvironment` subscribes | Process-wide events now include the instance events of `AppDomain.CurrentDomain` and `AssemblyLoadContext.Default` |
| `AttributeCollection.s_defaultAttributes` | Default attribute per attribute type; System.Web's attribute types are per domain | `FrameworkCaches` |
| `DbProviderFactories` registrations | A registration keeps the factory instance once requested. `FrameworkDefaults` did not replace an existing registration, so every later start used the first domain's `SqlClientFactory` (also wrong: a stale assembly copy) | Registrations with an instance from another domain go back to their type name, at domain start and at unload |
| `MemoryCache.Default` (System.Runtime.Caching, per domain) | Its stores and statistics timer sit in strong GC handles freed only by `Dispose`; .NET Framework disposed it on `DomainUnload`, .NET's no longer subscribes. MVC's model metadata and output cache use it | Disposed when the domain unloads; caches the app creates itself are its own to dispose |
| DataAnnotations `TypeDescriptorCache` (3 dictionaries) | `[MetadataType]` lookups by type, from MVC, Web API and System.Web model metadata; two keyed by tuples | `FrameworkCaches`; tuple keys are looked into |
| DataContractSerializer `s_typeToBuiltInContract` | `ContextAwareDictionary` keeps collectible types weakly but tests the type's assembly, so `IEnumerable<Product>` (a CoreLib generic over a domain type) lands in the strong half | `FrameworkCaches` (its `_fastDictionary`) |
| DataContractSerializer `s_dataContractCache` | The same test in the contract table by type number | Slots of domain types cleared and their numbers dropped from `s_typeToIDCache`, under the serializer's locks |
| `SerializationEventsCache`, `DataStorage`, `SqlUdtStorage` | Per-type `ConcurrentDictionary`s (BinaryFormatter events, DataTable column types) found by an inventory of the shared framework's static `Type`-keyed dictionaries; not seen as roots yet | `FrameworkCaches` |

`IsInDomain` now also counts generic instantiations and arrays over the domain's types, which are collectible with it.
The middleware's unload check interval is `Recycling.CheckInterval` (30 s); a domain is reported stuck 5 minutes after
its shutdown, whatever the interval.

Results: 3 restarts each, every old domain collected: IIS sample 3/3, MVC 3/3, Web API 3/3, Web Pages 3/3. Gate
tests now assert it (IIS, MVC and Web API: restart, then "(start 1) unloaded." within a minute; the samples check every
2 seconds through `GATE_RECYCLE_CHECK`).

`TypeDescriptor` needs nothing: .NET 11 keeps its tables by collectible-aware keys, and a provider registered for an
application type (`TypeDescriptor.AddProviderTransparent`, as MVC apps and DynamicData's `MetaModel` do) does not keep
the domain (data/Binding.aspx registers one; 2 of 2 old domains collected). Not covered yet: caches that hold app types
only on paths the samples don't use (`LicenseManager`, Hashtable-based caches that need their owner's lock). The
stuck-domain report and `MaxStuckDomains` remain as the safety net.

### Two intermittent test failures fixed (31 Oct 2026)

Both appeared once CI started running (on its fresh runners), and neither involves webForCore's code:
- **Identity.Test `CustomIntKeyTest.EnsureDefaultSchemaWithInt`** opened the test database without creating it, so it
  passed only when an earlier test had created it. Run first against CI's new SQL Server container it failed ("Cannot
  open database ... The login failed"). It now creates it, as its sibling tests do (fixup in `tools/lift/lift_aspnet.py`).
- **System.Web.Http.Test `HttpServerTest.SendAsync_SetsGenericPrincipalWhenThreadPrincipalIsNullAndCleansUpAfterward`**
  (and the other `[RestoreThreadPrincipal]` tests): the attribute sets the process-wide principal policy around each
  test, and on .NET a null `Thread.CurrentPrincipal` reads as whatever that policy gives (.NET Framework kept the null).
  A test class running in parallel resetting the policy made the test see a `GenericPrincipal`. The attribute now lets
  one such test run at a time (fixup in `tools/lift/lift_project.py`); 5 consecutive runs pass.

### Requests outlasting a restart: shutdownTimeout (31 Oct 2026)

A WebSocket held open across a restart kept echoing from the old domain for as long as the client stayed (tested 100 s),
and the domain was collected only when the client left. `AspNetCoreHost.Stop(immediate)` ignored `immediate` and
waited for the requests in progress without limit, so the hosting environment's shutdown (`Stop(false)`, wait up to
`<hostingEnvironment shutdownTimeout>`, `Stop(true)`, unload) never got past its first step. Now `Stop(false)`
unregisters once the requests finish, and `Stop(true)` aborts those left (`HttpContext.Abort`), as AppDomain.Unload's
thread aborts did on IIS. With the default 30 s, the socket was aborted 35 s after the restart and the domain collected.
The IIS sample sets 5 s, and `AppDomainTests.RestartAbortsAWebSocketAfterTheShutdownTimeout` checks the abort comes
after the timeout, not before or long after, and that the domain is collected.


## Typed DataSets and `<system.net>` (31 Oct 2026)

### Typed DataSets: `.xsd` in App_Code

`XsdBuildProvider` threw `NotImplementedException`: it generates code with System.Design's
`System.Data.Design.TypedDataSetGenerator`, which isn't open source. Now:
- **DataSet, table and row classes:** the Reference Source's older `System.Data.TypedDataSetGenerator`
  (`System.Data/System/Data/CodeGen/datacache.cs`, MIT), lifted by `tools/lift/lift_strb.py` into
  `src/WebFormsForCore.Web/WebFormsForCore/ReferenceSource/datacache.cs`. Its fixups make it internal, drop its
  HostProtection and Obsolete attributes, and replace the System.Data internals it used (`ColumnsReference`,
  `FindParentRelation`, `ConvertXmlToObject`, `ShouldSerializeLocale`, `_columnUri`, ...) with public members or
  `TypedDataSets/CodeGenSupport.cs`. .NET has no `TypedDataSetGeneratorException`; a `DataException` lists the errors.
- **What the designer generates and the old generator doesn't** (`TypedDataSets/TypedDataSetGenerator.cs`, a CodeDOM
  pass): partial classes, public table constructors and `XxxColumn` properties (App_Code and the pages compile into
  different assemblies), tables deriving from `TypedTableBase<row>` (typed `foreach` and LINQ), and the designer's
  names (`msprop:Generator_ColumnPropNameInRow`, `Generator_ChildPropName`, `Generator_ParentPropName`).
- **TableAdapters** (`TypedDataSets/TableAdapterGenerator.cs`, webForCore's own): read from the designer's annotation
  (`urn:schemas-microsoft-com:xml-msdatasource`). Per table: `Adapter`, `Connection`, `Transaction`,
  `CommandCollection`, `ClearBeforeFill`; Fill and Get methods per Rowset query (`GenerateMethods`); scalar and
  non-query methods; `Update` of a table, the DataSet, a row and rows; DB-direct `Insert`, `Update` and `Delete`
  (`GenerateShortCommands`), with `IsNull_` parameters and the shorter `Update` that passes the original key for the
  read-only key the statement refreshes. `QueriesTableAdapter` for the DataSet's own queries. `TableAdapterManager`
  (`EnableTableAdapterManager`): `UpdateAll` in one transaction, inserts and updates parents first, deletes children
  first, changes accepted after the commit, rows refreshed by their insert not updated again,
  `BackupDataSetBeforeUpdate`. The classes the provider's factory creates are used (`SqlConnection`, `SqlCommand`,
  `SqlDbType`), as the designer's code does; connection strings come from `<connectionStrings>`. CodeDOM only, so VB
  sites work too (the generated VB was compiled and run against SQL Server).
- Not generated: `GetTypedTableSchema` on the tables (a web service returning a typed DataTable),
  `TableAdapterManager`'s ordering of self-referencing tables.

Tests: the IIS sample has a designer DataSet (`App_Code/CatalogData.xsd`: two tables, a cascading relation, optimistic
concurrency, a parameterized query, a scalar query, a non-query, a stored procedure with an output parameter) and
`data/TypedDataSet.aspx`, which recreates the database on the CI's SQL Server and uses every kind of method, LINQ over
a typed table, an ObjectDataSource bound to a TableAdapter (select, and insert by parameter names) and
`TableAdapterManager.UpdateAll` (gate: `DataTests.TypedDataSetAndTableAdapters`). The restart test's workload includes
it; the old domain is still collected.

### `<system.net>`

.NET's System.Net reads no configuration (checked: `new SmtpClient()` gets no host from `<mailSettings>`,
`new MailMessage()` no From, `HttpClient.DefaultProxy` ignores `<defaultProxy>`). The fork's machine.config declared
`<defaultProxy>` and four other sections as `IgnoreSection`; they now have their Reference Source types, as in .NET
Framework's machine.config, so `GetSection` returns them. `WebFormsForCore.Net.Configuration.NetConfiguration`
(System.Configuration, public, MIT):
- `ApplyDefaultProxy()`: .NET Framework's `DefaultProxySectionInternal` (enabled, `proxyaddress`, `bypassonlocal`,
  the bypass list, `<module>`, `usesystemdefault`, `useDefaultCredentials`) into `HttpClient.DefaultProxy`, which
  HttpClient, WebClient, HttpWebRequest and SOAP client proxies use. Called by `HttpRuntime` when the application
  starts; each start begins from the process's original proxy (kept in `AppContext`). The system's proxy on Linux is
  .NET's default (`HTTP_PROXY`, `NO_PROXY`). WPAD and PAC scripts are ignored. A `<module>` proxy of the
  application's own type is replaced by the original when the domain unloads (`PseudoAppDomain.ReleaseDefaultProxy`).
- `CreateSmtpClient()`, `ApplyMailSettings(client)`, `DefaultFrom`: .NET Framework's `SmtpClient.Initialize` (host
  and port, credentials or default credentials, SSL, target name, delivery method and format, pickup directory) and
  the From of `new MailMessage()`. Used by `LoginUtil` (PasswordRecovery, CreateUserWizard, ChangePassword),
  `MailWebEventProvider`, `SmtpMailSender` (`System.Web.Mail`) and Web Pages' `WebMail` (fixup in
  `tools/lift/lift_project.py`). An application's own `new SmtpClient()` still gets nothing; MIGRATING.md says to use
  `CreateSmtpClient()`.
- `<connectionManagement>` is not applied: measured on .NET 11, `HttpWebRequest` opened 6 concurrent connections to one
  server whatever `ServicePointManager.DefaultConnectionLimit` or the `ServicePoint`'s `ConnectionLimit` (1 or 100), so
  .NET doesn't limit them, which is also what ASP.NET's `autoConfig` set (`DefaultConnectionLimit = Int32.MaxValue`).
  `<settings>`, `<webRequestModules>`, `<authenticationModules>` and `<requestCaching>` are readable but not applied.

A new holder of old domains came with it: `WebProxy` subscribes to `NetworkChange.NetworkAvailabilityChanged` the first
time it checks whether an address is local, and `NetworkChange` keeps each subscriber with the `ExecutionContext` it was
added in, here a request's, which carries the domain (heap dump: strong handle → `NetworkChange`'s subscriber dictionary
→ `ExecutionContext` → `AsyncLocalValueMap` → `PseudoAppDomain`). The gate's full run showed it: with net/Proxy.aspx
requested, the old domain was never collected. `PseudoAppDomain.ReleaseNetworkChangeSubscribers` drops those contexts
(and removes handlers of the domain's types), as `ReleaseTimers` does for timers; the old domain is collected again.

Tests (IIS gate, with a fake SMTP server and proxy in the test process): `MailTests.PasswordRecoveryMailsThroughMailSettings`
(a membership provider in App_Code, a PasswordRecovery postback; the message reaches the configured server from
`<smtp from>`), `NetTests.DefaultProxyFromConfiguration` (HttpClient and WebClient go through the configured proxy;
the bypass list and local addresses don't).

### Round 19 (31 Oct 2026)

27,297 tests in 24 suites (IIS gate 76: typed DataSets, PasswordRecovery mail, default proxy). 27,111 pass, 35
skipped, 151 known System.Drawing failures. The full run also failed
`Microsoft.Web.Mvc.Test TypeConverterModelBinderTest.BindModel_Error_FormatExceptionsTurnedIntoStringsInModelState_ErrorNotAddedIfCallbackReturnsNull`
(NotImplementedException from `ModelBinderConfigTest.DummyErrorSelector`): `ModelBinderConfigTest` sets
`ModelBinderConfig`'s static error message providers while xunit runs the classes that read them in parallel.
`ModelBinderConfigTest`, `TypeConverterModelBinderTest` and `MutableObjectModelBinderTest` now share a collection
(regex transform in `tools/lift/lift_project.py`); the suite passed three runs in a row.

## NuGet packages in CI and GitHub releases (31 Oct 2026)

The `WebFormsForCore.*` IDs on nuget.org belong to the upstream project, so webForCore's packages are `webForCore.*`:
the WebFormsForCore projects' PackageIds were renamed (`webForCore.Web` is System.Web), the lifted ones keep theirs,
and SignalR, Identity and System.Drawing.Common became packable (`lift_aspnet.py` and `lift_drawing.py` emit
PackageIds; before, `webForCore.Web` depended on a package named `System.Drawing.Common` at webForCore's version,
which is Microsoft's ID). `webForCore` (`src/webForCore`) is a package without code that brings the "Always" set of
MIGRATING.md. `webForCore.Web` carries `src/webForCore.WebStack.targets` as `buildTransitive/webForCore.Web.targets`, so
an app that uses the packages needs no `<Import>`. `WebFormsForCore.Build` (this repository's build tools) isn't
packed. Version 0.1.0 (was the upstream's 1.7.0-rc3); DataVisualization's README item had `Pack=" True"`.

CI packs the 49 packages on every run as `<Version>-ci.<run number>` and keeps them as the `packages` artifact; a tag
`v<Version>` (checked against Directory.Build.props) builds the release version and creates a GitHub release with the
packages attached. Nothing is pushed to nuget.org. `tools/ci/package-smoke.sh` builds `tools/ci/package-smoke`, an app
outside the repository's Directory.Build files that references only `webForCore` from the packed folder, runs it and
requests a page and an App_Code type: passed locally (0.1.0-ci.901). Packing the solution fails (its build
dependencies on WebFormsForCore.Build make NuGet call GetTargetPath on that multi-targeted project; the first CI run
stopped there), so `tools/ci/pack.sh` packs the solution's built `src/` projects one at a time.

A release's notes are the install instructions plus `docs/release-notes/<version>.md` when it exists (from 0.2.0: what
changed, and behaviour changes when upgrading).

## Fixes from migrating real applications (31 Oct 2026)

The smoke test bed ([jabba2324/webForCore-smoke-test](https://github.com/jabba2324/webForCore-smoke-test)) migrated
open-source ASP.NET 4.x applications with the 0.1.0 packages and filed what failed as issues #1-#15. Each fix below has
a regression test (most in `IIS.Gate.Tests`' `RegressionTests`, pages in `test/webForCore.IIS.Sample/regressions`) and
names the applications that hit it.

### HttpUtility and IHtmlString in application code (#7)

Code compiled against the fork's System.Web got `CS0433: The type 'HttpUtility' exists in both
'System.Web.HttpUtility' and 'System.Web'` (and the same for `IHtmlString`): the target that drops the shared
framework's `System.Web.HttpUtility.dll` was only in this repository's `Directory.Build.targets`, which applications
don't import. It is now in `src/webForCore.WebStack.targets` too (and so in the `webForCore.Web` package's
buildTransitive targets), for a System.Web from a project reference or the `webForCore.Web` package. Hit by
eShopLegacyWebForms and the Visual Studio Web Forms template (`ViewSwitcher.ascx.cs`), the Web API HelpPage area and
Bonobo Git Server. Test: `tools/ci/package-smoke` compiles `Markup.cs`, which uses both types (CS0433 on the 0.1.0
packages, passes on this branch's).

### ConfigurationManager from .NET Framework libraries (#1)

A library built for .NET Framework references `System.Configuration, Version=4.0.0.0`, the shared framework's facade,
which forwards to `System.Configuration.ConfigurationManager`. The PseudoAppDomain left the facade to the default
context, so its forwards resolved there too: a second ConfigurationManager, outside the domain, on the client
configuration system (no web.config), whose `ConfigurationSection` isn't the one System.Web's sections derive from, so
even `AppSettings` threw ("AppSettingsSection does not inherit from IConfigurationSectionHandler"). `System.Configuration`
is now in `PseudoAppDomain.PerDomainFrameworkAssemblies`: the facade is loaded into each domain and forwards to the
domain's ConfigurationManager. Hit by Microsoft.AspNet.SessionState.SessionStateModule (eShopLegacyMVC,
eShopLegacyWebForms: every request) and WebActivatorEx (Bonobo Git Server: the site never started). Test:
`RegressionTests.ConfigurationManagerThroughTheFrameworkFacadeReadsWebConfig` (`regressions/ConfigurationFacade.aspx`
binds ConfigurationManager by the facade's name and reads web.config's appSettings and `system.web/sessionState`).

### File change monitoring: one file under two spellings (#2)

On Linux a second `CacheDependency` on a file already monitored, reached through a different spelling of its path,
threw `ArgumentException: Item has already been added` from `DirectoryMonitor.AddFileMonitor`. The non-Windows branch
of `FindFileData.FindFile` set `FileNameLong` to the full path, where `FindFirstFile` gives the file's name; the
`DirectoryMonitor` keys its file monitors by `FileNameLong` but looks them up by name, so the second lookup missed and
`Hashtable.Add` threw. Per-file change notifications still worked only because the `FileSystemWatcher` that stands in
for `ReadDirectoryChangesW` also reported full paths. Both now use names as on Windows: `FindFile` returns the file
name (and, in the overload relative to a root, the relative path, without the parent walk through `FindFirstFile`),
and the watcher reports `FileSystemEventArgs.Name`, relative to the watched directory. That is also what
`GetFileMonitorForSpecialDirectory` (App_Code, bin, App_GlobalResources) compares against. Hit by eShopLegacyMVC:
`@Styles.Render`/`@Scripts.Render` when two first requests rendered the bundles. Test:
`RegressionTests.TwoDependenciesOnOneFileBothSeeItChange` (two dependencies on one file, then a write that both must
see).

### `<handlers>` paths that start with `/` (#12)

`WebServerModules.FindHandler` compared a handler path containing `/` with the request path without its leading `/`,
but kept the pattern's own, so `path="/metaweblog"` never matched `metaweblog`. Both sides are now compared without the
leading `/` (still anchored at the application root). Hit by MiniBlog: its XML-RPC endpoint (`/metaweblog`), feeds
(`/feed/*`), comment and post handlers (`/comment.ashx`, `/post.ashx`) were 404. Tests:
`RegressionTests.HandlerPathWithLeadingSlashMatches` (a wildcard and an exact path) and
`HandlerPathWithLeadingSlashDoesNotMatchDeeper`.

### URL Rewrite: Wildcard and ExactMatch, `{CACHE_URL}`, failing closed, query-only rewrites (#10, #11)

webForCore hands each level's `<rewrite><rules>` to ASP.NET Core's IIS rewrite parser, which has only ECMAScript
patterns and a fixed list of server variables. IIS's own "Remove WWW" template (`patternSyntax="Wildcard"`,
`{CACHE_URL}`) made it throw, and webForCore then skipped every rule of the level, so a rule that blocked a path
stopped blocking it. `InboundRewrite.TranslateRule` now rewrites each rule before parsing: Wildcard and ExactMatch
patterns become the equivalent regular expressions (as `OutboundRewrite` already did), `{CACHE_URL}` becomes a variable
the parser knows and webForCore otherwise doesn't serve (`{HTTP_PROXY_CONNECTION}`), which a server-variables feature
answers with IIS's CACHE_URL while the level's rules run, and a Rewrite to a query string alone (`?slug={R:1}`) gets the
level's root in front, where ASP.NET Core's `RewriteAction` threw `IndexOutOfRangeException` (#11). A redirect URL that
is absolute only once evaluated (`{C:1}://{C:2}`) no longer gets the level's path base in front. Rules that still can't
be parsed fail closed: every request below the level gets 500 (the error is logged), as IIS refuses configuration it
can't apply. Hit by MiniBlog: its ten rules, among them the one answering `posts/*.xml` with 403, were all skipped, so
the post files (commenters' e-mail addresses, drafts) were served; with the template working, its slug, paging and
category rewrites crashed (#11). Tests: `RegressionTests.WildcardRuleWithCacheUrlRedirects`,
`RulesNextToAWildcardRuleStillApply` (Wildcard and ExactMatch), `FilesTheRulesDontBlockAreServed`,
`UnusableRulesFailClosed` and `RewriteToAQueryStringRunsTheLevelsDefaultDocument` (`regressions/rewrite` and
`regressions/rewrite-unusable`); the 35 IISModuleTests still pass.

### The directory redirect and routes (#13)

The default-document step in front of the application answered every existing directory without a trailing slash
with IIS's courtesy 301, before System.Web's routing had seen the request. On IIS, `UrlRoutingModule` maps the request
to its route's handler first, and with `RouteExistingFiles` a route can take a URL that is also a directory; the
native redirect runs only for the static file handler. The host now passes the middleware a `RouteClaims` entry point
(`AspNetCoreHost.RouteClaims`, run in the application's domain: `RouteTable.Routes.GetRouteData` on the URL), and the
redirect is skipped when a route takes the URL. Also, a directory with rewrite rules of its own got its trailing slash
without a redirect (`InboundRewrite` ran `/dir` as the level's `/` and gave it back as `/dir/`); an unrewritten URL
now keeps its spelling. Hit by Clean Architecture in ASP.NET MVC 5: its controllers share their feature folders'
names (`Products/`, `Contact/`), so every link got a 301. Tests: `RegressionTests.RouteTakesADirectoryUrlWithRouteExistingFiles`
(the IIS sample's `Application_Start` sets `RouteExistingFiles` with one page route for an existing folder) and
`DirectoryWithoutARouteStillRedirects` (a plain folder and one with rewrite rules).

### W3C logs outside the site (#15)

`AddWebForms()`'s default `LogDirectory`, `Path.Combine(AppContext.BaseDirectory, "..", "logs", "W3SVC1")`, resolved
inside the site (the build output is a direct subfolder of it), and `logs` isn't hidden, so the site served its own
access log: client addresses and every URL with its query string. The default is now `$LOGS_DIRECTORY/W3SVC1` when
systemd sets it (`tools/deploy/webforcore@.service` gains `LogsDirectory=webforcore/%i`, which its `ReadWritePaths`
also needed: the old default wasn't writable under the unit), otherwise `logs/<site>/W3SVC1` next to the site folder.
A log folder an application chooses inside the site is added to the site's excluded directories, which now take
paths as well as names, and answers 404.8. Hit by every application the smoke test ran with the default
`AddWebForms()` (ten). Tests: `RegressionTests.W3CLogsInTheSiteAreNotServed` (the IIS sample now logs to a plain folder
of its own, `w3c-logs/W3SVC1`) and the package smoke test (the default: the log is next to the site, nothing is written
in it, and the old URL is 404).

### System.Web.DynamicData built and in the "Always" set (#8)

As on .NET Framework 4.5+, `GridView`, `ListView`, `Repeater`, `FormView`, `DetailsView`, `CheckBoxList` and
`RadioButtonList` call `DataControlExtensions.EnableDynamicData` in `OnInit` whenever `ItemType` is set, so every
strongly-typed data control loads System.Web.DynamicData. It wasn't built (`<Build Project="false" />` in the
solution), and every such control failed with `FileNotFoundException`. It builds now: the Entity Framework 1–4 model
providers (`EF*Provider.cs`, on System.Data.Entity's `ObjectContext`) are compiled out as the LINQ to SQL ones already
were, and so are the matching branches of `SchemaCreator`, `DynamicDataManager` (`LinqDataSource`, `EntityDataSource`),
`DynamicDataExtensions.LoadWith`, and the EF 5 spatial column types in `MetaColumn` and `FieldTemplateFactory`
(`#if NETFRAMEWORK`). `AssemblyRef.cs` names the designer assembly its attributes refer to (as System.Web.Mobile's
does), and `ScaffoldTableAttribute`, which .NET dropped from System.ComponentModel.DataAnnotations, is supplied with the
Reference Source's members. The unused `Microsoft.EntityFrameworkCore` package reference is gone. The `webForCore`
package (and MIGRATING.md's "Always" table) includes it. Hit by eShopLegacyWebForms (its catalog is a ListView with
`ItemType`) and the Visual Studio 2012+ Web Forms templates' model-bound pages. Tests:
`RegressionTests.DataControlsWithItemTypeRender` (Repeater, ListView and GridView with `ItemType`, the last two through
`SelectMethod` over a type with a `[MetadataType]` buddy class) and the package smoke test's `Typed.aspx`.

### `AppDomain.CurrentDomain.BaseDirectory` (#3): documented, not changed

On IIS each application runs in its own AppDomain, created with `ApplicationBase` set to the site's folder and `bin` as
its private bin path, so `AppDomain.CurrentDomain.BaseDirectory` is the site root. .NET has one AppDomain per process:
`BaseDirectory` is `AppContext.BaseDirectory`, the entry assembly's folder, `bin_dotnet/`. A PseudoAppDomain is an
`AssemblyLoadContext` and has no base directory of its own. Making it the site root would mean setting
`APP_CONTEXT_BASE_DIRECTORY` for the whole process, which moves what .NET libraries rely on (native libraries, data files
next to the assemblies) and what eighteen places in webForCore use to find the build output, Microsoft's
`HttpRuntime` and `ClientConfigurationHost` among them. So the difference is kept and documented (NAMESPACES.md,
MIGRATING.md section 6), with the APIs that are the site root as on IIS: `HttpRuntime.AppDomainAppPath`,
`HostingEnvironment.ApplicationPhysicalPath`, `Server.MapPath("~/")`. Hit by eShopLegacyMVC, whose database seed read
its SQL scripts through `BaseDirectory`. Test: `RegressionTests.BaseDirectoryIsTheBuildOutputAndTheSiteApisAreTheSite`
pins both, so the documentation can't silently go out of date.

## Fixes from the smoke test's second round (31 Oct 2026)

The smoke test bed's second round ran on the 0.2.0 packages. All 14 applications from the first round now pass, and
five more were migrated (ApplicantTracking, OpenOrderFramework, ReaLocate, Roadkill, SocialGoal). What failed was
filed as issues #17-#23. As before, each fix has a regression test and names the applications that hit it.

### Configuration types through the System facade (#19)

The first round's fix for #1 loads the `System.Configuration` facade per domain. The configuration types that were in
`System.dll` on .NET Framework (`IConfigurationSectionHandler`, `SingleTagSectionHandler`, `NameValueSectionHandler`,
`ConfigurationException`, ...) are reached through the `System` facade instead, and that was still shared from the
default context, so its forwards resolved to the host's ConfigurationManager. ELMAH 1.2's section handlers derive from
`SingleTagSectionHandler` in `System, Version=2.0.0.0`, and SocialGoal failed on every request with "Type
'Elmah.ErrorMailSectionHandler' does not inherit from 'System.Configuration.IConfigurationSectionHandler'". `System`
is now in `PerDomainFrameworkAssemblies`; its other forwards go to shared-framework assemblies, which the domain still
shares. Test: `RegressionTests.SectionHandlerThroughTheSystemFacadeIsTheApplications`
(`regressions/SystemFacadeSection.aspx` and a `SingleTagSectionHandler` section in `regressions/web.config`; a 500
on 0.2.0).

### The System.Web.Abstractions and System.Web.Routing facades (#20)

On .NET Framework 4 the abstractions (`HttpContextBase` and the rest) and routing are in System.Web, and the 3.5
assemblies that held them are facades in the GAC that forward there. web.configs from MVC 2-4 projects list both in
`<compilation><assemblies>` (Version=4.0.0.0), and .NET 3.5 libraries reference them, so a missing facade is a
Configuration Error on every request: Roadkill's web.config has both. Two new projects, `src/WebFormsForCore.Web.Abstractions`
and `src/WebFormsForCore.Web.Routing`, build the facades with their names, version 4.0.0.0 and public key token, and
type forwarders for the types of the 3.5 assemblies. They are new code, not Microsoft's, and part of the "Always" set (the
`webForCore` package depends on `webForCore.Web.Abstractions` and `webForCore.Web.Routing`). Test:
`RegressionTests.SystemWebAbstractionsAndRoutingFacadesLoad` (`regressions/facades/`, whose web.config lists both;
it binds `HttpContextBase` and `RouteTable` by the 3.5 names).

### webForCore's packages win the SDK's conflicts (#23)

Packages built on MVC, Web API, Katana or WebActivatorEx bring Microsoft's packages back transitively (Unity.Mvc 5.11
brings Microsoft.AspNet.Mvc 5.2.7, Razor 3.2.7 and Web Pages 3.2.7; Autofac.Mvc5 3.3 brings 5.1.0 and 3.1.0). The SDK's
conflict resolution keeps one assembly of each name, by assembly version and then file version. MVC 5.3.1 beats 5.2.7,
but webForCore's `System.Web.Razor` and `System.Web.WebPages` are 3.0.0.0 like Microsoft's, and Microsoft's file
versions (3.0.20110, 3.0.61128) are higher, so they replaced webForCore's in the output with no build error. MVC views
then failed with a `MissingMethodException` (Bonobo Git Server, ApplicantTracking, ReaLocate, Roadkill; eShopLegacyMVC
without its `ExcludeAssets` workaround). The SDK's preferred packages only break ties after the file version, but its
package overrides come first (they are how the shared framework wins over the System.* packages). So the
`WebForCoreOverrideReplacedPackages` target in `src/webForCore.WebStack.targets` (the packages' buildTransitive
targets) makes each webForCore package in the build override the Microsoft packages webForCore replaces, at any
version. It makes the `ExcludeAssets` step in MIGRATING.md unnecessary for applications on the packages. Test:
`tools/ci/package-smoke-mvc`, an MVC application with `Unity.Mvc` 5.11.1 as applications have it. The package smoke
test checks that its `deps.json` has none of the .NET Framework assemblies; on the 0.2.0 packages it has Razor 3.2.7's
and Web Pages 3.2.7's.

### WebActivatorEx (#21)

WebActivatorEx (by David Ebbo, Apache 2.0; used by Unity.Mvc, Ninject.MVC, StructureMap.MVC, Glimpse and many
application templates) finds its `PreApplicationStartMethod`, `PostApplicationStartMethod` and
`ApplicationShutdownMethod` attributes with `Assembly.LoadFrom` on every DLL in `HttpRuntime.BinDirectory`. On .NET,
`LoadFrom` always loads into the default `AssemblyLoadContext`. In a PseudoAppDomain it returned second copies of the
application's assemblies, whose attributes are the default context's WebActivatorEx types, so none matched and no
method ran, with no error. ApplicantTracking's Unity resolver was never registered ("No parameterless constructor
defined" for every controller); Roadkill's StructureMap container stayed null. .NET has no hook for `LoadFrom`, so
webForCore ships its own WebActivatorEx: `src/WebFormsForCore.WebActivatorEx`, written from the documented API, with the
original's name, version 2.0.0.0 and public key. It loads the assemblies by name into its own context (the
application's), and scans only those that reference WebActivatorEx (read from their metadata, without loading the
rest). It is in the "Always" set; the original package, which Unity.Mvc and others bring, loses the conflict to it
(#23's `WebForCoreOverrideReplacedPackages`). New code, not Microsoft's. `docs/IIS-COMPATIBILITY.md` lists `Assembly.LoadFrom` as
a difference. Test: `tools/ci/package-smoke-mvc`, where Unity.Mvc's activator registers the resolver that constructs
`HomeController`, and a post-start method runs ("No parameterless constructor defined" on the 0.2.0 packages).

### System.Net.Http.WebRequest for Katana's social providers (#18)

0.2.0's guide said to keep the application's `Microsoft.Owin.Security.Google` (and Facebook, MicrosoftAccount, Twitter)
package, which webForCore doesn't lift (#14). That builds, but each provider's middleware does
`options.BackchannelHttpHandler ?? new WebRequestHandler()`, and `WebRequestHandler` is in .NET Framework's
`System.Net.Http.WebRequest.dll`, which .NET doesn't have. As soon as a provider was registered, every request,
static files included, failed in `OwinHttpModule.Init` with "Could not load file or assembly
'System.Net.Http.WebRequest, Version=4.0.0.0'" (OpenOrderFramework and SocialGoal, which call
`app.UseGoogleAuthentication()`). Rather than lift four providers, `src/WebFormsForCore.Net.Http.WebRequest` builds
`System.Net.Http.WebRequest` 4.0.0.0 with .NET Framework's name and public key: a `WebRequestHandler` that is an
`HttpClientHandler`, written from its documented API. It covers the original packages at every version from 2.0, and
WS-Federation and third-party providers built the same way. `ServerCertificateValidationCallback`, which the providers
set from `BackchannelCertificateValidator`, maps to `ServerCertificateCustomValidationCallback`; the settings only
`HttpWebRequest` had are kept and ignored. New code, not Microsoft's, in the "Always" set. Test:
`tools/ci/package-smoke-mvc`'s `Startup` registers Katana 3.0.1's Google provider, and `/challenge` must redirect to
accounts.google.com (a 500 on the 0.2.0 packages).

## Only Microsoft assemblies: third-party libraries move to webForCore-thirdparty (31 Oct 2026)

webForCore now ships only Microsoft assemblies (CONTRIBUTING.md, principle 1). Two non-Microsoft libraries were in
the repository and its packages:

- **WebActivatorEx** (David Ebbo's, Apache 2.0), added in PR #24 as webForCore's own implementation of its API under
  its name and public key, in the "Always" set (#21).
- **AjaxControlToolkit** (CodePlex Foundation, BSD 3-Clause), inherited from upstream (a submodule there, vendored
  here), packed as `webForCore.AjaxControlToolkit`, `.StaticResources` and `.HtmlEditor.Sanitizer` since 0.1.0.

Both moved to [jabba2324/webForCore-thirdparty](https://github.com/jabba2324/webForCore-thirdparty), keeping their
package IDs, built against webForCore's released packages (0.3.0) for net11.0 only. That repository exists only to help
the smoke test migrate applications that use them: **nothing in webForCore depends on it**, not its code, packages,
tests, CI or user documentation. Only AjaxControlToolkit's three shipped projects moved; its sample site, Jasmine
tests, Visual Studio packages and tools stay in DevExpress's archived repository. There, WebActivatorEx is delay signed
with the original's public key (a public-key-only `.snk`) instead of webForCore's FakeStrongName task, and each package
declares its own `PackageConflictOverrides` over the original package. Its `tools/smoke` app (Unity.Mvc started by a
WebActivatorEx start method, and an AjaxControlToolkit `CalendarExtender`) passes on webForCore 0.3.0.

Here: the projects, their solution entries, the `webForCore` package's reference to WebActivatorEx, their entries in
`webForCore.WebStack.targets`' package overrides, the `Directory.Build.targets` item that gave AjaxControlToolkit
webForCore's System.Drawing.Common, and the TestApp's AjaxControlToolkit page are removed. #21 is a documented
difference again: MIGRATING.md section 3 and IIS-COMPATIBILITY.md say to call WebActivatorEx's start methods from
`Application_Start`, and AGENT-MIGRATION.md lists the symptom. `tools/ci/package-smoke-mvc` keeps Unity.Mvc and the
original WebActivatorEx package, as applications have them (for #23), and starts Unity from `Application_Start`; the
original package's `ActivationManager` runs and finds nothing, and the app must still work. NOTICE, LICENSING.md,
LICENSE-BSD-3-CLAUSE.txt and NAMESPACES.md follow. The build-and-run steps above no longer fetch the AjaxControlToolkit
submodule.
BundleTransformer had already been removed from the repository (LICENSING.md); a leftover checkout of it is ignored.

## Fixes from the smoke test's third round (1 Nov 2026)

The third round ran on the 0.4.0 packages. 17 of
the 19 applications from before pass. Two more were migrated: aspnet-mvc5-starter passes; MVCForum found #26–#29 and
#31. Each fix below has a test and names the application.

### configSource in a subfolder (#26)

`<security configSource="Config\imageprocessor\security.config" />` (MVCForum's ImageProcessor sections) was a
Configuration Error on every request. `InternalConfigHost.StaticGetStreamNameForConfigSource`, from the Reference Source,
combines the path with `Path.Combine`, and on Linux `\` isn't a directory separator. Writing it with `/` isn't an
option: System.Configuration rejects that, as on .NET Framework. So a `configSource` file in a subfolder could never be
found. The method now takes `\` as the separator off Windows (the client host already did, in
`ClientConfigurationHost`). Microsoft's code changed: a guarded `Replace`, with a `webForCore:` comment; in
MODIFICATIONS.md. Test: `RegressionTests.ConfigSourceInASubfolderIsRead` (`regressions/configsource/`, whose web.config
has `<appSettings configSource="Config\settings.config" />`; a 500 on 0.4.0).

### relaxedUrlToFileSystemMapping (#27)

With `<httpRuntime relaxedUrlToFileSystemMapping="true">`, System.Web replaces a mapped physical path that has any of
`s_InvalidPhysicalPathChars` with `<site>/NOT_A_VALID_FILESYSTEM_PATH`. The list, from .NET Framework, includes `/`,
which is in every physical path on Linux, so every `MapPath` failed: MVCForum's `HostingEnvironment.MapPath("~/App_Data/forum.config")`
during its database seed. `/` is left out of the list when it is the directory separator. Microsoft's code changed:
the array's initializer, with a `webForCore:` comment; in MODIFICATIONS.md. Test: `tools/ci/package-smoke` turns the
setting on and `MapPath.aspx` must map `~/Default.aspx` to the file (on the 0.4.0 packages, every page of the app fails).

### System.Web.Helpers with MVC (#28)

`Microsoft.AspNet.Mvc` depends on `Microsoft.AspNet.WebPages`, which contains `System.Web.Helpers.dll`, so every MVC 5
application has it, and web.configs list it. `webForCore.Web.Mvc` didn't depend on `webForCore.Web.Helpers`, and MVCForum,
whose packages don't bring Microsoft's, failed with "Could not load file or assembly 'System.Web.Helpers'". The lifted
MVC project references the Helpers project (`fork_refs` in `tools/lift/lift_project.py`), so the package depends on it.
Re-running the lift for System.Web.Mvc produces that reference. It also showed that the script wrote the spike's
references to the fork's own projects as lifted siblings (`../webForCore.WebFormsForCore.Web/...`, which doesn't exist,
where the committed project has `../WebFormsForCore.Web/...`): `lift_project.py` now keeps a reference to a project in
`src/<Name>/<Name>.csproj` as it is, and re-running the lift reproduces the committed project and files exactly. Test: `tools/ci/package-smoke-mvc`
lists System.Web.Helpers in web.config and uses `Json.Encode` in a view. Its `deps.json` must have webForCore's
`System.Web.Helpers`: on 0.4.0 it has `Microsoft.AspNet.WebPages`' `lib/net45` one, which Unity.Mvc brings.

### Migration guide (#29, #30, #31)

`docs/MIGRATING.md`:
- `System.Web.ApplicationServices` is visible to every project, so a library's own `MembershipUser` can be ambiguous
  (#29, MVCForum).
- More versions webForCore requires: Humanizer.Core 2.14.1, and the System.* packages webForCore brings at 11.0 (#30,
  aspnet-mvc5-starter and MVCForum).
- A package's .NET Standard build can have less than its `net45` build: WindowsAzure.Storage 9.x, Hangfire 1.6 (#31,
  aspnet-mvc5-starter and MVCForum).

## Windows as a development environment (#46, 1 Nov 2026)

Most teams with ASP.NET 4.x applications develop on Windows, so webForCore now builds, runs and passes its tests there
too. Linux stays the production target. CI has a `build-windows` job on `windows-2025` that runs `tools/ci/run-tests.sh`
under Git Bash, packs, and runs the package smoke test.

### Making the scripts run

`pack.sh` strips the CR that Windows' Python ends lines with. `package-smoke.sh` writes the Windows form of the package
folder into `nuget.config` (`pwd -W`). `run-tests.sh` sets `PYTHONUTF8=1`, because `check_failures.py` failed printing
a test name with `∞` in Windows' code page.

### What failed, and the fixes

The first run failed 6 suites (Identity 196, System.Drawing 565, IIS gate 7, WebPages.Deployment 9, MVC and Web API
gates 1 each).

- **SQL Server:** service containers are Linux only. `tools/ci/sqlserver-windows.ps1` installs SQL Server Express on
  `127.0.0.1,14333` with the `sa` login, so the tests' connection strings don't change.
- **Deployment tests' paths:** the lift had turned Microsoft's `@"a\b"` test paths into `"a/b"`, which makes mixed paths
  on Windows that System.Web rejects as non-canonical (`IsSuspiciousPhysicalPath`). `lift_project.py` now writes
  `Path.Combine("a", "b")`.
- **Default documents:** on Windows `File.Exists` ignores case, so `default.aspx` from the default-document list was
  rewritten as typed, not as the file's name (`Default.aspx`). `IISModules.FindFile` now looks the name up on Windows
  too.
- **Protected configuration:** on Windows the providers use Windows' own key containers and DPAPI, as on .NET Framework.
  The tests of the key files and the emulation run off Windows (`NotOnWindowsFact`, with the reason). The Windows DPAPI
  test checks Windows' own error.
- **System.Drawing:** Microsoft's tests choose their expectations by OS (`PlatformDetection.IsWindows`,
  `[PlatformSpecific]`), meaning "GDI+ is Windows' own". Here it is SkiaGdiPlus on both, so `lift_drawing_tests.py`
  makes Windows answer as Linux (its fixups now apply to arcade's files too). Windows runs the 3,882 tests Linux runs,
  with the same 151 known failures. `FontNameConverterTest` keeps its real OS check, because "Times" matching
  "Times New Roman" is about Windows' fonts.
- **Identity:** `LockoutEndToNowPlus5ShouldNotBeLockedOut` assumes Pacific time. The suite runs with
  `TZ=America/Los_Angeles`, which .NET on Windows ignores, so the test computes Pacific time itself.

### App domains that didn't unload on Windows

After a restart, the old PseudoAppDomain was never collected on Windows (one test in each gate, two in the IIS gate).
The investigation ran on a Windows Server 2025 VM:

- dumps of the sample (`dotnet-dump`);
- a small ClrMD analyzer that walks from the GC's real roots, with named static and thread-static fields. SOS's
  `gcroot` lists the domain's own static-storage handles as roots, which hides the external one.

Six Windows-only references, each found in a dump and each fix confirmed by a run:

1. **System.Web's telemetry:** `TelemetryLogger` wrote `EventSource` events with System.Web's own types.
   - On Windows they're really written: `TelemetryEventSource` is in a provider group that Windows' telemetry service
     enables.
   - TraceLogging then caches each event's type in a thread static of the writing thread (`TraceLoggingTypeInfo.threadCache`),
     and the type keeps the domain alive.
   - webForCore writes no telemetry: the logger is a no-op.
2. **HttpClient's system proxy:** `HttpWindowsProxy` waits for changes to the proxy settings on the `ExecutionContext`
   it was created on, which was the first request's. The host now creates it before the first domain, without a
   context (`WebFormsMiddleware.StartProcessWideWaits`).
3. **NetworkChange's listener:** on Windows its address-change wait keeps the `ExecutionContext` of the first subscriber
   (WebProxy, during a request). The host now subscribes first, without a context, and stays subscribed.
4. **NetworkChange's subscribers:** `ReleaseNetworkChangeSubscribers` locked on `s_gate`, which only the Unix
   implementation has, so on Windows it released nothing. It now uses `s_globalLock` there.
   - It also runs at every unload check, not only when unloading starts, because a request still running then can
     subscribe later.
   - A context now counts as the domain's when its `AsyncLocal` values reach one of the domain's objects (System.Web's
     call context). This is a bounded walk of fields that runs no code of the objects.
5. **SqlClient's pool:** on Windows a pooled connection's native SNI handle holds its state object, and with it the
   domain, in a strong GC handle. `PseudoAppDomain` clears the domain's SqlClient pools when it starts unloading,
   as AppDomain.Unload discarded them.
6. A crash found on the way: runtime-async continuations throw from `GetType()`, so the field walk skips them. It runs
   on the recycling timer, where an exception would stop the process.

Results:

- **Windows:** every suite passes, and System.Drawing has its 151 known failures. The two Linux-only protected
  configuration tests are skipped.
- **Linux:** unchanged.
- **CI:** the Windows job runs beside Linux's on every push, pull request and tag, and fails on a failing test as
  Linux's does. Both are required checks on `webforcore-lift`, and a release needs both.
