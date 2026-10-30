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
  checked-in TestApp `*.deps.json` files (generated artefacts), and BundleTransformer and NativeDlls (not in the solution).

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

### Unit tests (net11.0, Linux)

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
