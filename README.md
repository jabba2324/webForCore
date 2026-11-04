# webForCore

**Run ASP.NET 4.x applications on .NET 10 (LTS), Kestrel and Linux, without rewriting them.**

webForCore runs Web Forms, MVC 5, Web API 2, Web Pages, SignalR 2, OWIN/Katana and ASP.NET Identity 2 apps on
.NET 10. It is built from Microsoft's own code: System.Web and System.Configuration from the .NET Framework reference
source (through [WebFormsForCore](https://github.com/webformsforcore/WebFormsForCore), which this project forks), and
the ASP.NET libraries Microsoft open-sourced. Those libraries are compiled from submodules of Microsoft's repositories
(cleared-down forks in the [xforcore](https://github.com/xforcore) org) and tested with Microsoft's own test suites.
The parts IIS used to provide (request filtering, URL Rewrite, default documents, custom errors, compression, logging)
run in front of the app on Kestrel and are configured from the app's existing `<system.webServer>` section, and
`webforcore-host` plays the application pool's part: it runs the app in worker processes and recycles them when the
app restarts.

webForCore ships only Microsoft assemblies, and nothing in it depends on a third-party build of a library.

The reference behaviour is .NET Framework 4.8 on IIS 10. Where .NET 10 behaves differently and webForCore can't
restore the old behaviour, the difference is documented.

| | |
| --- | --- |
| Target | .NET 10 LTS (`net10.0`), Linux x64; Windows as a development environment (built and tested in CI) |
| Status | Pre-release (0.7.0). No production app has been migrated yet. NuGet packages are on a [feed on GitHub Pages](https://xforcore.github.io/webForCore/index.json) (from 0.7.0) and attached to [GitHub releases](https://github.com/xforcore/webForCore/releases), not published on nuget.org |
| Tests | 23,598 tests in 25 suites, almost all of them Microsoft's and Mono's; all pass ([TESTING.md](docs/TESTING.md)). System.Drawing's own tests are [drawingForCore](https://github.com/xforcore/drawingForCore)'s |
| Licence | MIT, Apache 2.0 and BSD 3-Clause, all of which allow commercial use; see [docs/LICENSING.md](docs/LICENSING.md) |

## What works

| Framework | Assemblies | Notes |
| --- | --- | --- |
| Web Forms | System.Web, System.Web.Extensions (AJAX), System.Web.Services (ASMX), DataVisualization (Chart), DynamicData, Mobile | Pages, user controls, master pages, App_Code, Global.asax, themes; compiled at run time by Roslyn, or precompiled |
| MVC 5 | System.Web.Mvc 5.3, Razor 3, Web Pages 3, Helpers, MVC Futures | `.cshtml` views compiled at run time as on IIS |
| Web API 2 | System.Web.Http 5.3, WebHost, OWIN host, CORS, System.Net.Http.Formatting | |
| OWIN | Microsoft.Owin 4.2.3, Host.SystemWeb, Security (Cookies, OAuth), Hosting, HttpListener, Testing, Cors | |
| SignalR 2.4.3 | Core, SystemWeb, .NET client | WebSockets, Server-Sent Events, long polling |
| ASP.NET Identity 2 | Core, EntityFramework (EF 6.5), Owin | |
| Data | System.Data.SqlClient 4.9.1 | Microsoft's, built from its source with `Assembly.GetTypes()` fixed (#41); replaces the `System.Data.SqlClient` package that Entity Framework 6 brings |
| Configuration | System.Configuration (web.config, machine.config, protected sections) | DPAPI and RSA key containers work on Linux |
| Drawing | System.Drawing, System.Drawing.Common 6.0 | [drawingForCore](https://github.com/xforcore/drawingForCore)'s packages: Microsoft's managed code over a GDI+ engine written on SkiaSharp; no libgdiplus |
| Optimization | System.Web.Optimization, WebGrease | |

What doesn't work, and why, is in [docs/NAMESPACES.md](docs/NAMESPACES.md). The main gaps are Windows
authentication and impersonation, WCF service hosting, metafiles (EMF/WMF), and Windows-only APIs (registry, COM,
Code Access Security).

## Documentation

| Document | Contents |
| --- | --- |
| [docs/MIGRATING.md](docs/MIGRATING.md) | Step-by-step migration of an existing app |
| [docs/AGENT-MIGRATION.md](docs/AGENT-MIGRATION.md) | The same migration as instructions for an AI coding agent |
| [docs/NAMESPACES.md](docs/NAMESPACES.md) | Which .NET Framework namespaces are implemented, by assembly, with caveats |
| [docs/IIS-COMPATIBILITY.md](docs/IIS-COMPATIBILITY.md) | The IIS features rebuilt on Kestrel: what's supported and what isn't |
| [docs/TESTING.md](docs/TESTING.md) | The test suites, where they come from, results and code coverage |
| [docs/MODIFICATIONS.md](docs/MODIFICATIONS.md) | Where Microsoft's code comes from (the `external/` submodules) and what had to change |
| [docs/LICENSING.md](docs/LICENSING.md) | Which parts have which licence, commercial use, and what was replaced to get there |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute: a test and a real application for every change, Microsoft's code left alone |

## Migrating an app: the short version

The full guide is [docs/MIGRATING.md](docs/MIGRATING.md). In outline:

1. **Get webForCore:** add webForCore's and drawingForCore's NuGet feeds (from 0.7.0) to `nuget.config`, or download
   the packages from a [GitHub release](https://github.com/xforcore/webForCore/releases) into a folder, and reference
   `webForCore` (see [docs/MIGRATING.md](docs/MIGRATING.md#1-get-webforcore)). Or build it and reference its projects:
   ```bash
   git clone --recurse-submodules https://github.com/xforcore/webForCore && cd webForCore
   dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release   # build tasks the other projects import
   dotnet build WebFormsForCore.slnx -c Release
   ```
2. **Make the app an SDK-style `Microsoft.NET.Sdk.Web` project** targeting `net10.0`, with its output in a direct
   subfolder (`bin_dotnet`), as classic ASP.NET's `bin` was:
   ```xml
   <PropertyGroup>
     <TargetFramework>net10.0</TargetFramework>
     <OutputType>Exe</OutputType>
     <OutputPath>bin_dotnet</OutputPath>
     <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
     <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
     <PredefinedCulturesOnly>true</PredefinedCulturesOnly>
     <!-- MVC 5 / Web Pages: views are compiled at run time, not by the ASP.NET Core Razor SDK -->
     <EnableDefaultRazorGenerateItems>false</EnableDefaultRazorGenerateItems>
     <RazorCompileOnBuild>false</RazorCompileOnBuild>
     <RazorCompileOnPublish>false</RazorCompileOnPublish>
   </PropertyGroup>
   <!-- With project references only; the packages bring it: -->
   <Import Project="path/to/webForCore/src/webForCore.WebStack.targets" />
   <ItemGroup>
     <Compile Remove="App_Code/**;bin_dotnet/**" />
     <Content Remove="bin_dotnet/**" />
     <None Remove="bin_dotnet/**" />
   </ItemGroup>
   ```
   Replace the .NET Framework references (System.Web, MVC, Web API, OWIN packages) with webForCore's packages or
   projects: see [docs/MIGRATING.md](docs/MIGRATING.md#3-references).
3. **Add a `Program.cs`:**
   ```csharp
   var builder = WebApplication.CreateBuilder(args);
   builder.Services.AddWebForms();                 // IIS compression, W3C logging
   builder.Services.AddDistributedMemoryCache();   // if the app uses session state
   var app = builder.Build();
   app.UseWebForms(o => o.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider());
   app.Run();
   ```
4. **Keep `web.config`**, `<system.webServer>` included. Set an explicit `<machineKey>` so cookies and view state
   survive restarts.
5. **Run it** from the app folder under the host, `dotnet bin_dotnet/webforcore-host/webforcore-host.dll --urls
   http://localhost:5000`, and compare its responses with IIS. The host restarts the app in a new worker process when
   `web.config`, `bin` or `App_Code` changes, as IIS did. `dotnet bin_dotnet/YourApp.dll` also runs it, as a single
   process that stops when the app restarts.

## Related repositories

| Repository | What it is |
| --- | --- |
| [webForCore-smoke-test](https://github.com/xforcore/webForCore-smoke-test) | Real open-source ASP.NET 4.x applications migrated with webForCore's release packages, following [docs/MIGRATING.md](docs/MIGRATING.md), then built, run and probed over HTTP. Its findings become this repository's issues, and its results are in its `RESULTS.md` |
| [webForCore-thirdparty](https://github.com/xforcore/webForCore-thirdparty) | Builds of third-party libraries for webForCore (WebActivatorEx, WebActivator 1.x, AJAX Control Toolkit), made for the smoke test from webForCore's released packages |
| [drawingForCore](https://github.com/xforcore/drawingForCore) | System.Drawing.Common 6.0 on .NET 10 for Linux, macOS and Windows, with GDI+ written on SkiaSharp, and Microsoft's tests for it. Moved out of this repository (#53); webForCore uses its packages |

The first two build on webForCore's releases; webForCore doesn't depend on them. webForCore depends on drawingForCore.

## Building and testing this repository

Requirements: the .NET SDK in `global.json` (10.0.100 or a later 10.0 SDK), and on Fedora/RHEL `OPENSSL_ENABLE_SHA1_SIGNATURES=1`
(strong-name signing uses SHA-1; without it `csc` fails with `OpenSslCryptographicException: ... invalid digest`). The Identity tests need SQL Server; `tools/ci/sqlserver.sh` starts one in a
container. System.Drawing comes from [drawingForCore](https://github.com/xforcore/drawingForCore)'s packages, restored
from its feed (`nuget.config`); to try a drawingForCore change here before it is released, build against a checkout
of it with `-p:DrawingForCoreSource=../drawingForCore`.

```bash
dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build WebFormsForCore.slnx -c Release
tools/ci/sqlserver.sh               # SQL Server for Identity.Test (podman or docker)
tools/ci/run-tests.sh                 # every suite, as CI runs it
```

CI (`.github/workflows/ci.yml`) runs the same steps on Ubuntu and on Windows for every push and pull request. On Windows,
run them in Git Bash, with SQL Server Express in place of the container ([TESTING.md](docs/TESTING.md#running-the-tests)).
To check that a build is really clean, run `dotnet build-server shutdown` first or pass `-nodeReuse:false`: reused
MSBuild nodes cache imported `.targets` files.

The packages are `webForCore.*`, because the `WebFormsForCore.*` IDs on nuget.org belong to the upstream project.
`webForCore.Web` is System.Web, and carries `src/webForCore.WebStack.targets` as `buildTransitive`, so an app needs no
`<Import>`; `webForCore` is the "Always" set of [MIGRATING.md](docs/MIGRATING.md#3-references) without code of its own.
`tools/ci/pack.sh` packs the built `src/` projects one at a time (packing the solution fails on the multi-targeted
build-tools project).

Microsoft's code is compiled from the submodules in `external/`: cleared-down forks of the upstream repositories,
with webForCore's changes as commits on each fork's `webforcore` branch. The projects in `src/webForCore.*` and
`test/webForCore.*` are webForCore's; to change Microsoft's code, see [docs/MODIFICATIONS.md](docs/MODIFICATIONS.md)
and CONTRIBUTING.md rule 5.

## Origins and credits

webForCore is a fork of [WebFormsForCore](https://github.com/webformsforcore/WebFormsForCore) by Simon Jakob Egli,
which ported System.Web and its companions from the .NET Framework reference source to .NET and first ran Web Forms
on ASP.NET Core. webForCore adds Microsoft's open-source ASP.NET libraries and their tests, the IIS layer, the host
and its worker processes, System.Drawing on SkiaSharp (now [drawingForCore](https://github.com/xforcore/drawingForCore)),
and the compatibility work described in [docs/](docs/) and the [release notes](docs/release-notes/). The engineering
log kept while the code was lifted in was removed in #67 and is in git history; the repository
[at 35aeecff](https://github.com/xforcore/webForCore/tree/35aeecff) is the last commit that has it.

The upstream code is © Microsoft and the .NET Foundation (ASP.NET Web Stack, Katana, SignalR, ASP.NET Identity) and
the Mono project (System.Web tests). See [NOTICE](NOTICE) for every component, its source and its licence.
