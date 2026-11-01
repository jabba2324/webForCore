# What webForCore changed in lifted code and tests

webForCore runs Microsoft's own ASP.NET 4.x libraries rather than reimplementations of them. Where Microsoft published
the source, it is **lifted**: copied file by file from the upstream repository at a pinned commit, built for .NET 11,
and tested with the upstream project's own tests. This page explains the rules for those copies and what had to change.

- The full per-file list, with a link from each changed file to its upstream original, is generated:
  **[lifted-changes.md](lifted-changes.md)** (`tools/lift/changes_report.py`).
- The reasoning for every change, phase by phase, is in [`LIFT.md`](../LIFT.md).
- Licences and attributions are in [`NOTICE`](../NOTICE) and [`LICENSE`](../LICENSE).

## Rules for lifted code

1. **Copy, don't rewrite.** Each file is copied as it is upstream and keeps its copyright header. The Web Stack copies
   have their line endings normalised to CRLF; nothing else changes on copy.
2. **Every change is scripted and listed.** Changes are search-and-replace pairs in the lift scripts (`FIXUPS` in
   `tools/lift/lift_project.py`, `lift_aspnet.py`, `lift_drawing.py`, `lift_drawing_tests.py`, `lift_mono_tests.py`,
   `lift_configuration.py`, `lift_appservices.py`, `lift_aspnet_compiler.py`, `lift_resx.py`, `lift_strb.py`).
   A pair that no longer matches upstream stops the lift. Most changes carry a `// webForCore:` comment that gives the
   reason.
3. **Additions are kept separate.** Code webForCore wrote is in `tools/lift/additions/` and is copied in as new files,
   for example the GDI+ engine for System.Drawing and `ResponseContent` for Web API.
4. **Every file is traceable.** `tools/lift/manifests/*.tsv` records each lifted file's upstream path and commit.
5. **Identity is kept.** Assemblies keep Microsoft's names and versions. Those Microsoft strong-named are delay signed
   with Microsoft's public key (`src/SigningKey/35MSSharedLib1024.snk`), so existing references and binding
   information resolve to them. They aren't signed by Microsoft.
6. **Project files are generated** by the lift scripts. The lifted directories are output: change the script and lift
   again, rather than editing them.
7. **.NET Framework 4.8 is the reference.** A change restores 4.8's behaviour where .NET differs. Where the difference
   comes from the .NET runtime itself (the BCL) and a test asserted the old behaviour, the test is corrected to .NET's
   behaviour and the difference is documented in `LIFT.md`.

## Lifted components

| Component | Upstream (pinned commit) | Licence | Files lifted | Files changed |
| --- | --- | --- | ---: | ---: |
| ASP.NET Web API 2, Razor 3, Web Pages 3, MVC 5, MVC Futures, Helpers, CORS | [aspnet/AspNetWebStack@2106565](https://github.com/aspnet/AspNetWebStack/tree/2106565) | Apache 2.0 | 1,553 libraries · 1,266 tests | 26 · 121 |
| Katana (Microsoft.Owin 4.2.3) | [aspnet/AspNetKatana@096e225](https://github.com/aspnet/AspNetKatana/tree/096e225) | Apache 2.0 | 324 · 55 | 8 · 3 |
| ASP.NET SignalR 2.4.3 | [SignalR/SignalR@7f53f26](https://github.com/SignalR/SignalR/tree/7f53f26) | Apache 2.0 | 318 · 152 | 2 · 1 |
| ASP.NET Identity 2 | [aspnet/AspNetIdentity@b7fc094](https://github.com/aspnet/AspNetIdentity/tree/b7fc094) | MIT | 75 · 26 | 0 · 4 |
| System.Drawing.Common 6.0 | [dotnet/runtime@76b9680](https://github.com/dotnet/runtime/tree/76b9680) (release/6.0) | MIT | 199 | 4, plus the new engine |
| System.Drawing.Common tests, TestUtilities | dotnet/runtime@76b9680, [dotnet/arcade@565a800](https://github.com/dotnet/arcade/tree/565a8005), [dotnet/runtime-assets@848e27e](https://github.com/dotnet/runtime-assets/tree/848e27e) | MIT | 233 | 2 |
| System.Web tests | [mono/mono@0f53e9e](https://github.com/mono/mono/tree/0f53e9e) (`mcs/class/System.Web/Test`) | MIT | 188 | 30 |
| System.Configuration, System.Web.ApplicationServices, aspnet_compiler, `BindableTypeAttribute` (replacing WebFormsForCore's decompiled copies); System.Data's typed DataSet generator | [microsoft/referencesource@ec9fa9a](https://github.com/microsoft/referencesource/tree/ec9fa9ae770d522a5b5f0607898044b7478574a3) | MIT | 114 | 41 |
| ResX classes (replacing decompiled copies) | [dotnet/winforms@8266870](https://github.com/dotnet/winforms/tree/8266870) (release/8.0) | MIT | 13 | 13 |
| StronglyTypedResourceBuilder (replacing an API reconstruction) | mono/mono@0f53e9e (`mcs/class/System.Design`) | MIT | 1 | 1 |
| **Total** | | | **4,517** | **256** |

The fork itself starts from [webformsforcore/WebFormsForCore@52f4172](https://github.com/webformsforcore/WebFormsForCore/tree/52f4172dbc09b3ad0b849c97642720fa7a49aa22),
whose System.Web, System.Configuration and related assemblies were ported from Microsoft's .NET Framework reference
source. Some of that port had been decompiled from .NET Framework assemblies instead; webForCore replaced those parts
with the MIT originals (the last four rows above) or its own code ([LICENSING.md](LICENSING.md#what-was-replaced)).
webForCore's changes to the port are listed at the end of [lifted-changes.md](lifted-changes.md).

## Library changes, by kind

| Kind | Examples | Where |
| --- | --- | --- |
| **APIs .NET doesn't have** | `AppDomain.DefineDynamicAssembly` → `AssemblyBuilder.DefineDynamicAssembly` (SignalR); `AppDomainSetup.PrivateBinPath` → scan the app base (Katana's loader); LINQ to SQL's `Binary` binding compiled out (MVC, Web Pages); `NetDataContractSerializer`, which MVC Futures' `MvcSerializer` uses, is replaced by a `DataContractSerializer` with a type-preserving resolver (`tools/lift/additions/Microsoft.Web.Mvc`), so `MvcSerializer.cs` itself is unchanged; Code Access Security policy removed | Web Stack, Katana, SignalR |
| **Windows-only APIs** | The CryptoAPI certificate context → `PublicKey.ExportSubjectPublicKeyInfo()` (Katana); `DpapiDataProtector` → webForCore's DPAPI emulation (`LocalDataProtection`); the HttpListener host's private-field access → Katana's Mono path | Katana |
| **.NET Framework behaviour restored** | Web Pages' `WebMail` takes its defaults from `<system.net><mailSettings>`, which .NET's `SmtpClient` ignores; `float`/`double` route constraints reject overflow; a whitespace multipart root path is rejected; `HttpResponseMessage.Content` is never null on .NET, so `ResponseContent.Get` treats `EmptyContent` as no content (11 call sites in the Web API hosts); a request body copied through the seekable stream; `HostString` keeps the host's case | Web Stack, Katana |
| **Compiler and language** | A `(string)null` where .NET 11's `ArraySegment<T>` overloads made `null` ambiguous; `LangVersion` pinned to what Microsoft built with (C# 7.3 for SignalR) | SignalR, Web Stack |
| **Linux and ICU** | Case-correct paths for files referenced with a different case; newline handling; IDN mapping | Web Stack tests, Katana |
| **System.Drawing on SkiaSharp** | System.Drawing.Common 6.0's managed code is unchanged, except that the GDI+ P/Invokes are pointed at a managed GDI+ (`SkiaGdiPlus`, in `tools/lift/additions/System.Drawing.Common/`) instead of libgdiplus | dotnet/runtime |

## Test changes, by kind

Tests change only to fit the platform. A failing test is never edited just to make it pass: either the library is
fixed, or the BCL difference is documented.

| Kind | Examples |
| --- | --- |
| **BCL message and type differences** | `ArgumentException.Message` ends with `(Parameter 'name')`; `MediaTypeHeaderValue(null)` throws `ArgumentNullException`; `System.Private.CoreLib` instead of `mscorlib` in assembly-qualified names; the UTF-8 decoder's replacement characters |
| **Platform newline and paths** | Razor's generated code and error messages use `\n` on Linux; tests that built Windows paths build them with `Path.Combine`, which gives each platform's separator (#46) |
| **Test infrastructure** | `RunInSeparateAppDomain` and `AppDomainUtils` on collectible `AssemblyLoadContext`s (`tools/lift/additions/Microsoft.TestCommon`); xunit 1.9 → 2 (`Assert.ThrowsDelegate`, `Xunit.Extensions`); `PersistedAssemblyBuilder` for `AssemblyBuilderAccess.Save` |
| **Windows-only test dependencies** | DPAPI in Katana's security tests → `TestDataProtectionProvider` (AES-CBC and HMAC-SHA256); LocalDB → SQL Server in a container (Identity); a test certificate .NET's DER reader rejects outside Windows |
| **Time zone** | Identity's `LockoutEndToNowPlus5ShouldNotBeLockedOut` assumes Pacific time: the suite runs with `TZ`, and the test takes Pacific time itself, since .NET on Windows ignores `TZ` (#46) |
| **Mono's System.Web tests** | Corrected to Microsoft's .NET Framework 4.5+ behaviour wherever Mono's own implementation differed (Mono's tests follow Mono); hosting through the fork's PseudoAppDomain |
| **System.Drawing's tests** | The four "is GDI+ available" checks answer for the managed engine instead of probing for libgdiplus. The suite's Windows checks (`PlatformDetection.IsWindows`, `[PlatformSpecific]`, two direct OS checks) mean "GDI+ is Windows' own", which it never is here, so Windows answers as Linux and runs the same tests (#46); the font-name test keeps its real OS check, since it depends on Windows' installed fonts |
| **Selection** | Microsoft's own categories are used (`category!=failing` and the non-Linux categories for System.Drawing). SignalR's IIS Express and Service Bus tests aren't lifted |

## webForCore's changes to System.Web and the hosting layer

These aren't lifts. They change WebFormsForCore's port, and each has a `webForCore:` comment and an entry in `LIFT.md`.

- **Hosting:** the PseudoAppDomain (AppDomain emulation), IIS's `<system.webServer>` modules, the Kestrel worker
  request (WebSockets, client certificates, server variables, timeouts, integrated-pipeline APIs). See
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md).
- **Integrated-mode semantics** on the classic pipeline: writable `Response.Headers`, the Content-Type rule, the
  server name from the Host header.
- **Configuration:** protected configuration on Linux (DPAPI emulation, RSA key containers as files), section type
  identity inside a PseudoAppDomain, `machine.config` refreshed by content.
- **Compilation:** the in-process compiler loads into the app's context; precompilation for .NET
  (`System.Private.CoreLib` as the core library); typed DataSets in App_Code (the Reference Source's
  `System.Data.TypedDataSetGenerator` and webForCore's TableAdapter generator).
- **`<system.net>`:** `<defaultProxy>` applied at application start, and `<mailSettings>` used by System.Web's mail
  senders (`WebFormsForCore.Net.Configuration.NetConfiguration`).
- **BinaryFormatter port:** surrogates for .NET Framework types .NET no longer serializes (`NameValueCollection`
  and others), so session state and view state round-trip.
- **Security:** `bin`, `App_Data`, the build output folder and the other hidden segments return 404, as on IIS.
- **File change notifications off Windows** (`FileChangesMonitor.cs`, `Util/FileUtil.cs`): `FindFileData` names a
  file as `FindFirstFile` does (its name, or its path relative to the monitored root, without the Windows-only parent
  walk), and the `FileSystemWatcher` that replaces `ReadDirectoryChangesW` reports names relative to the watched
  directory, as `ReadDirectoryChangesW` does (#2).
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
