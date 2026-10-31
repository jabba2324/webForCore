# webForCore

**Run ASP.NET 4.x applications on .NET 11, Kestrel and Linux, without rewriting them.**

webForCore runs Web Forms, MVC 5, Web API 2, Web Pages, SignalR 2, OWIN/Katana and ASP.NET Identity 2 apps on
.NET 11. It is built from Microsoft's own code: System.Web and System.Configuration from the .NET Framework reference
source (through [WebFormsForCore](https://github.com/webformsforcore/WebFormsForCore), which this project forks), and
the ASP.NET libraries Microsoft open-sourced. Those libraries are lifted file by file from Microsoft's repositories
and tested with Microsoft's own test suites. The parts IIS used to provide (request filtering, URL Rewrite, default
documents, custom errors, compression, logging, AppDomain recycling) run in front of the app on Kestrel and are
configured from the app's existing `<system.webServer>` section.

The reference behaviour is .NET Framework 4.8 on IIS 10. Where .NET 11 behaves differently and webForCore can't
restore the old behaviour, the difference is documented.

| | |
| --- | --- |
| Target | .NET 11 (`net11.0`), Linux x64 (Windows builds too) |
| Status | Pre-release (0.2.0). No production app has been migrated yet. NuGet packages are attached to [GitHub releases](https://github.com/jabba2324/webForCore/releases), not published on nuget.org |
| Tests | 27,297 tests in 24 suites, almost all of them Microsoft's and Mono's; all pass except 151 known System.Drawing failures, 141 of them metafiles ([TESTING.md](docs/TESTING.md)) |
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
| Configuration | System.Configuration (web.config, machine.config, protected sections) | DPAPI and RSA key containers work on Linux |
| Drawing | System.Drawing.Common 6.0 | Microsoft's managed code over a GDI+ engine written on SkiaSharp; no libgdiplus |
| Optimization | System.Web.Optimization, WebGrease, AjaxControlToolkit | |

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
| [docs/MODIFICATIONS.md](docs/MODIFICATIONS.md) | How Microsoft's code was lifted and what had to change, with links to upstream |
| [docs/lifted-changes.md](docs/lifted-changes.md) | Generated list of every lifted file that differs from upstream |
| [docs/LICENSING.md](docs/LICENSING.md) | Which parts have which licence, commercial use, and what was replaced to get there |
| [LIFT.md](LIFT.md) | The engineering log: every change, decision and test result, in order |
| [CONTRIBUTING.md](CONTRIBUTING.md) | How to contribute: a test and a real application for every change, Microsoft's code left alone |

## Migrating an app: the short version

The full guide is [docs/MIGRATING.md](docs/MIGRATING.md). In outline:

1. **Get webForCore:** download the packages from a [GitHub release](https://github.com/jabba2324/webForCore/releases)
   into a folder, add it as a package source in `nuget.config`, and reference `webForCore` (see
   [docs/MIGRATING.md](docs/MIGRATING.md#1-get-webforcore)). Or build it and reference its projects:
   ```bash
   git clone https://github.com/jabba2324/webForCore && cd webForCore
   dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release   # build tasks the other projects import
   dotnet build WebFormsForCore.slnx -c Release
   ```
2. **Make the app an SDK-style `Microsoft.NET.Sdk.Web` project** targeting `net11.0`, with its output in a direct
   subfolder (`bin_dotnet`), as classic ASP.NET's `bin` was:
   ```xml
   <PropertyGroup>
     <TargetFramework>net11.0</TargetFramework>
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
   builder.Services.AddWebForms();                 // IIS compression, W3C logging, systemd integration
   builder.Services.AddDistributedMemoryCache();   // if the app uses session state
   builder.Services.AddSession();
   var app = builder.Build();
   app.UseSession();
   app.UseWebForms(o => o.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider());
   app.Run();
   ```
4. **Keep `web.config`**, `<system.webServer>` included. Set an explicit `<machineKey>` so cookies and view state
   survive restarts.
5. **Run it** from the app folder with `dotnet bin_dotnet/YourApp.dll`, and compare its responses with IIS.

## Building and testing this repository

Requirements: the .NET SDK in `global.json` (11.0.100-rc.3), and on Fedora/RHEL `OPENSSL_ENABLE_SHA1_SIGNATURES=1`
(strong-name signing uses SHA-1). The Identity tests need SQL Server; `tools/lift/sqlserver.sh` starts one in a
container.

```bash
dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build WebFormsForCore.slnx -c Release
tools/ci/fetch-test-fonts.sh          # fonts for System.Drawing's tests (not redistributable)
tools/lift/sqlserver.sh               # SQL Server for Identity.Test (podman or docker)
tools/ci/run-tests.sh                 # every suite, as CI runs it
```

CI (`.github/workflows/ci.yml`) runs the same steps on Ubuntu for every push and pull request.

The lifted directories (`src/webForCore.*`, `test/webForCore.*`) are generated by the scripts in `tools/lift/`. To
change one, change the script and lift again; see [docs/MODIFICATIONS.md](docs/MODIFICATIONS.md).

## Origins and credits

webForCore is a fork of [WebFormsForCore](https://github.com/webformsforcore/WebFormsForCore) by Simon Jakob Egli,
which ported System.Web and its companions from the .NET Framework reference source to .NET and first ran Web Forms
on ASP.NET Core. webForCore adds the lifted Microsoft libraries and their tests, the IIS layer, AppDomain emulation,
System.Drawing on SkiaSharp, and the compatibility work recorded in [LIFT.md](LIFT.md).

The lifted code is © Microsoft and the .NET Foundation (ASP.NET Web Stack, Katana, SignalR, ASP.NET Identity,
System.Drawing.Common) and the Mono project (System.Web tests). See [NOTICE](NOTICE) for every component, its source
and its licence.
