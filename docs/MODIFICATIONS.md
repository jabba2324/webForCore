# What webForCore changed in Microsoft's code and tests

webForCore runs Microsoft's own ASP.NET 4.x libraries rather than reimplementations of them. Where Microsoft published
the source, webForCore compiles it from a **submodule** in `external/`: a fork of the upstream repository, cleared down
to the files webForCore uses, at a pinned commit, built for .NET 10 and tested with the upstream project's own tests.
(Until #61 the files were copied in by lift scripts; git history has that record.) This page explains the rules for
that code and what had to change.

- **Every change is a commit** on the fork's `webforcore` branch, with its reason in the message:

  | Submodule | Fork branch | Upstream commit | Changes |
  | --- | --- | --- | --- |
  | `external/AspNetWebStack` | [xforcore/AspNetWebStack `webforcore`](https://github.com/xforcore/AspNetWebStack/commits/webforcore) | `2106565` | 149, a casing fix, and upstream's `.gitattributes` kept (Windows line endings for the tests) |
  | `external/AspNetKatana` | [xforcore/AspNetKatana `webforcore`](https://github.com/xforcore/AspNetKatana/commits/webforcore) | `096e225` | 10 |
  | `external/SignalR` | [xforcore/SignalR `webforcore`](https://github.com/xforcore/SignalR/commits/webforcore) | `7f53f26` | 3 |
  | `external/AspNetIdentity` | [xforcore/AspNetIdentity `webforcore`](https://github.com/xforcore/AspNetIdentity/commits/webforcore) | `b7fc094` | 5 |
  | `external/referencesource` | [xforcore/referencesource `webforcore`](https://github.com/xforcore/referencesource/commits/webforcore) | `ec9fa9a` | 41 |
  | `external/winforms` | [xforcore/winforms `webforcore`](https://github.com/xforcore/winforms/commits/webforcore) | `8266870` | 13 |
  | `external/mono` | [xforcore/mono `webforcore`](https://github.com/xforcore/mono/commits/webforcore) | `0f53e9e` | 31 |

  Each branch starts at the upstream commit, then one commit keeps only the files webForCore uses (and the licences),
  then the changes, one file per commit. `git log` on a submodule lists them; `git revert` undoes one.
- Licences and attributions are in [`NOTICE`](../NOTICE) and [`LICENSE`](../LICENSE).
- System.Drawing.Common 6.0 and its tests were part of webForCore until they moved to
  [drawingForCore](https://github.com/xforcore/drawingForCore) (#53), whose `docs/MODIFICATIONS.md` lists their changes.

## Rules for Microsoft's code

1. **Don't rewrite.** Each file is upstream's, with its copyright header, at the pinned commit.
2. **Every change is a commit on the fork,** one problem per commit, most with a `// webForCore:` comment that gives the
   reason (CONTRIBUTING.md rule 5). webForCore then moves the submodule to it, with the test.
3. **webForCore's own files stay in the projects,** for example `ResponseContent` for Web API, next to the `Link` items
   that compile the submodule's files at their old paths.
4. **Every file is traceable:** the submodule pins the fork's commit, and the fork's history goes back to upstream's.
5. **Identity is kept.** Assemblies keep Microsoft's names and versions. Those Microsoft strong-named are delay signed
   with Microsoft's public key (`src/SigningKey/35MSSharedLib1024.snk`), so existing references and binding
   information resolve to them. They aren't signed by Microsoft.
6. **Project files are webForCore's,** edited by hand: they name the submodule files they compile.

## Components from submodules

| Component | Upstream (pinned commit) | Licence | Files used | Files changed |
| --- | --- | --- | ---: | ---: |
| ASP.NET Web API 2, Razor 3, Web Pages 3, MVC 5, MVC Futures, Helpers, CORS | [aspnet/AspNetWebStack@2106565](https://github.com/aspnet/AspNetWebStack/tree/2106565) | Apache 2.0 | 1,553 libraries · 1,266 tests | 26 · 121 |
| Katana (Microsoft.Owin 4.2.3) | [aspnet/AspNetKatana@096e225](https://github.com/aspnet/AspNetKatana/tree/096e225) | Apache 2.0 | 324 · 55 | 8 · 3 |
| ASP.NET SignalR 2.4.3 | [SignalR/SignalR@7f53f26](https://github.com/SignalR/SignalR/tree/7f53f26) | Apache 2.0 | 318 · 152 | 2 · 1 |
| ASP.NET Identity 2 | [aspnet/AspNetIdentity@b7fc094](https://github.com/aspnet/AspNetIdentity/tree/b7fc094) | MIT | 75 · 26 | 0 · 4 |
| System.Web tests | [mono/mono@0f53e9e](https://github.com/mono/mono/tree/0f53e9e) (`mcs/class/System.Web/Test`) | MIT | 188 | 30 |
| System.Configuration, System.Web.ApplicationServices, aspnet_compiler, `BindableTypeAttribute` (replacing WebFormsForCore's decompiled copies); System.Data's typed DataSet generator | [microsoft/referencesource@ec9fa9a](https://github.com/microsoft/referencesource/tree/ec9fa9ae770d522a5b5f0607898044b7478574a3) | MIT | 114 | 41 |
| ResX classes (replacing decompiled copies) | [dotnet/winforms@8266870](https://github.com/dotnet/winforms/tree/8266870) (release/8.0) | MIT | 13 | 13 |
| StronglyTypedResourceBuilder (replacing an API reconstruction) | mono/mono@0f53e9e (`mcs/class/System.Design`) | MIT | 1 | 1 |
| **Total** | | | **4,085** | **250** |

The fork itself starts from [webformsforcore/WebFormsForCore@52f4172](https://github.com/webformsforcore/WebFormsForCore/tree/52f4172dbc09b3ad0b849c97642720fa7a49aa22),
whose System.Web, System.Configuration and related assemblies were ported from Microsoft's .NET Framework reference
source. Some of that port had been decompiled from .NET Framework assemblies instead; webForCore replaced those parts
with the MIT originals (the last four rows above) or its own code ([LICENSING.md](LICENSING.md#what-was-replaced)).
webForCore's changes to the port are its git history since the fork: `git diff 52f4172 -- 'src/WebFormsForCore.*'`.

## Library changes, by kind

| Kind | Examples | Where |
| --- | --- | --- |
| **APIs .NET doesn't have** | `AppDomain.DefineDynamicAssembly` → `AssemblyBuilder.DefineDynamicAssembly` (SignalR); `AppDomainSetup.PrivateBinPath` → scan the app base (Katana's loader); LINQ to SQL's `Binary` binding compiled out (MVC, Web Pages); `NetDataContractSerializer`, which MVC Futures' `MvcSerializer` uses, is replaced by a `DataContractSerializer` with a type-preserving resolver (`src/webForCore.Microsoft.Web.Mvc`), so `MvcSerializer.cs` itself is unchanged; Code Access Security policy removed | Web Stack, Katana, SignalR |
| **Windows-only APIs** | The CryptoAPI certificate context → `PublicKey.ExportSubjectPublicKeyInfo()` (Katana); `DpapiDataProtector` → webForCore's DPAPI emulation (`LocalDataProtection`); the HttpListener host's private-field access → Katana's Mono path | Katana |
| **.NET Framework behaviour restored** | Web Pages' `WebMail` takes its defaults from `<system.net><mailSettings>`, which .NET's `SmtpClient` ignores; `float`/`double` route constraints reject overflow; a whitespace multipart root path is rejected; `HttpResponseMessage.Content` is never null on .NET, so `ResponseContent.Get` treats `EmptyContent` as no content (11 call sites in the Web API hosts); a request body copied through the seekable stream; `HostString` keeps the host's case | Web Stack, Katana |
| **Compiler and language** | A `(string)null` where .NET's `ArraySegment<T>` overloads made `null` ambiguous; `LangVersion` pinned to what Microsoft built with (C# 7.3 for SignalR) | SignalR, Web Stack |
| **Linux and ICU** | Case-correct paths for files referenced with a different case; newline handling; IDN mapping | Web Stack tests, Katana |

## Test changes, by kind

Tests change only to fit the platform. A failing test is never edited just to make it pass: either the library is
fixed, or the BCL difference is documented.

| Kind | Examples |
| --- | --- |
| **BCL message and type differences** | `ArgumentException.Message` ends with `(Parameter 'name')`; `MediaTypeHeaderValue(null)` throws `ArgumentNullException`; `System.Private.CoreLib` instead of `mscorlib` in assembly-qualified names; the UTF-8 decoder's replacement characters; stack frames: a test asserts on MVC's own frame, not on .NET's Activator frames, which the JIT may inline (#74) |
| **Platform newline and paths** | Razor's generated code and error messages use `\n` on Linux; tests that built Windows paths build them with `Path.Combine`, which gives each platform's separator (#46) |
| **Test infrastructure** | `RunInSeparateAppDomain` and `AppDomainUtils` on collectible `AssemblyLoadContext`s (`test/webForCore.Microsoft.TestCommon`); xunit 1.9 → 2 (`Assert.ThrowsDelegate`, `Xunit.Extensions`); `PersistedAssemblyBuilder` for `AssemblyBuilderAccess.Save` |
| **Windows-only test dependencies** | DPAPI in Katana's security tests → `TestDataProtectionProvider` (AES-CBC and HMAC-SHA256); LocalDB → SQL Server in a container (Identity); a test certificate .NET's DER reader rejects outside Windows |
| **Time zone** | Identity's `LockoutEndToNowPlus5ShouldNotBeLockedOut` assumes Pacific time: the suite runs with `TZ`, and the test takes Pacific time itself, since .NET on Windows ignores `TZ` (#46) |
| **Mono's System.Web tests** | Corrected to Microsoft's .NET Framework 4.5+ behaviour wherever Mono's own implementation differed (Mono's tests follow Mono); hosting in the test process (.NET has one AppDomain) |
| **Selection** | Microsoft's own categories are used (`category!=failing`). SignalR's IIS Express and Service Bus tests aren't built |

## webForCore's changes to System.Web and the hosting layer

These aren't in the submodules. They change WebFormsForCore's port, and each has a `webForCore:` comment; the
commit that made it, and its issue, say why.

- **Hosting:** webforcore-host and its worker processes (process recycling, #56), IIS's `<system.webServer>` modules,
  the Kestrel worker request (WebSockets, client certificates, server variables, timeouts, integrated-pipeline APIs).
  See [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md).
- **Integrated-mode semantics** on the classic pipeline: writable `Response.Headers`, the Content-Type rule, the
  server name from the Host header.
- **Configuration:** protected configuration on Linux (DPAPI emulation, RSA key containers as files), section type
  identity across load contexts, `machine.config` refreshed by content.
- **Compilation:** the in-process compiler loads into the app's context, and so does `AssemblyBuilder` with every
  compiled assembly (never CodeDom's `Assembly.LoadFile`, which loaded App_Code a second time, #56); precompilation for .NET
  (`System.Private.CoreLib` as the core library); typed DataSets in App_Code (the Reference Source's
  `System.Data.TypedDataSetGenerator` and webForCore's TableAdapter generator); `BuildManager.ResolveAssembly` looks the
  fixed names (`App_GlobalResources`, `App_Code`, `__code`) up by simple name too, because .NET passes the display name,
  so `Assembly.Load("App_GlobalResources")` finds the compiled assembly, which keeps its random name as on IIS (#33).
- **`<system.net>`:** `<defaultProxy>` applied at application start, and `<mailSettings>` used by System.Web's mail
  senders (`WebFormsForCore.Net.Configuration.NetConfiguration`).
- **Build targets** (`src/webForCore.WebStack.targets`, imported by every app through the `webForCore.Web` package):
  - The shared framework has facades named `System.Web.dll` and `System.Configuration.dll`, and the host keeps whichever
    copy has the higher `assemblyVersion` in `deps.json`. The SDK writes none for project references, so the
    framework's 4.0.0.0 facade won and loading System.Web failed with 0x80131053 ("The requested assembly version
    conflicts with what is already bound"). A target after `GenerateBuildDependencyFile` writes each app-local
    assembly's real `assemblyVersion` where a reference pack has one of the same name.
  - The framework's `System.Web.HttpUtility.dll` (and the `System.Web` facade) are left out of the compiler's references
    next to webForCore's System.Web (CS0433, CS7069 otherwise). They are matched by the reference pack's name, not by
    the SDK's `packs` folder: an SDK newer than the target framework takes the pack from the NuGet cache
    (`microsoft.netcore.app.ref/<version>/`). `tools/ci/package-smoke.sh` builds once that way.
- **Run-time compilation** adds the targets of a referenced facade's type forwards (CS0012 otherwise), leaving out
  those whose types System.Web defines itself (`HttpUtility`).
- **BinaryFormatter port:** surrogates for .NET Framework types .NET no longer serializes (`NameValueCollection`
  and others), so session state and view state round-trip.
- **Security:** `bin`, `App_Data`, the build output folder and the other hidden segments return 404, as on IIS.
- **File change notifications off Windows** (`FileChangesMonitor.cs`, `Util/FileUtil.cs`): `FindFileData` names a
  file as `FindFirstFile` does (its name, or its path relative to the monitored root, without the Windows-only parent
  walk), and the `FileSystemWatcher` that replaces `ReadDirectoryChangesW` reports names relative to the watched
  directory, as `ReadDirectoryChangesW` does (#2). `bin` and the critical directories (`App_Code`, ...) are watched
  on every platform, as in the reference source; WebFormsForCore watched them only on Windows (`HttpRuntime.cs`, #51).
- **No ASP.NET telemetry** (`TelemetryLogger.cs`): .NET Framework wrote handler, provider and target-framework events
  to Windows' telemetry (`TelemetryEventSource`, in a provider group Windows' telemetry service enables). webForCore
  writes none; on Windows, TraceLogging's per-thread type cache also kept an application's domain alive (#46).
- **Paths with `/` as the separator:** `configSource` paths written with `\` resolve on Linux
  (`InternalConfigHost.StaticGetStreamNameForConfigSource`, #26), and `/` no longer makes a mapped physical path invalid
  under `relaxedUrlToFileSystemMapping` (`HttpRuntime.s_InvalidPhysicalPathChars`, #27).
- **System.Web.DynamicData** (`src/WebFormsForCore.Web.DynamicData`, now built and in the "Always" set): the Entity
  Framework 1–4 and LINQ to SQL model providers, `LinqDataSource`/`EntityDataSource` hookups and EF 5 spatial column
  types are compiled out (`#if NETFRAMEWORK`); `AssemblyRef.cs` and `ScaffoldTableAttribute` (which .NET dropped) are
  added (#8).
