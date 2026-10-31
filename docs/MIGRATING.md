# Migrating an ASP.NET 4.x app to webForCore

This guide takes an existing Web Forms, MVC 5, Web API 2 or Web Pages application and runs it on .NET 11 with
webForCore, on Linux or Windows. The approach keeps the application's code, markup and `web.config`: the project
becomes an SDK-style .NET 11 app whose `Program.cs` hosts the same ASP.NET pipeline on Kestrel.

For an AI agent doing the migration, the same procedure is written as instructions in
[AGENT-MIGRATION.md](AGENT-MIGRATION.md).

## Before you start

1. **Check the blockers.** Search the solution for the APIs in the summary table of [NAMESPACES.md](NAMESPACES.md):
   `WindowsIdentity`, `<authentication mode="Windows">`, `System.ServiceModel` hosting (`.svc`), `System.Data.Linq`,
   `ObjectContext`/EDMX (EF 1–4), registry and COM use, `App_WebReferences`,
   EMF/WMF metafiles. Each one needs a code change; plan these first.
2. **Inventory the dependencies.** List NuGet packages and referenced DLLs:
   - Microsoft's ASP.NET packages (`Microsoft.AspNet.Mvc`, `.WebApi.*`, `.WebPages`, `.Razor`, `.Cors`,
     `Microsoft.Owin.*`, `.SignalR.*`, `.Identity.*`, `Microsoft.AspNet.Web.Optimization`, `AjaxControlToolkit`) are
     replaced by webForCore's projects.
   - Pure managed libraries that target .NET Standard or .NET usually work as they are. Newtonsoft.Json, Autofac,
     EntityFramework 6.4+ and log4net are examples.
   - Libraries built only for .NET Framework that reference System.Web often load, because webForCore's assemblies
     carry the same names. Test each one. Closed-source control suites need checking with the vendor.
3. **Record the reference behaviour.** With the app running on IIS, capture the responses of its main pages and APIs:
   status, headers and body. You'll compare against these later.
4. **Set `targetFramework`.** `<httpRuntime targetFramework="4.5">` or later is the behaviour webForCore reproduces.
   If the app has no `targetFramework`, add one and test on IIS first.

## 1. Get webForCore

**From the packages (simplest).** Each release on GitHub
([Releases](https://github.com/jabba2324/webForCore/releases)) has webForCore's NuGet packages attached; they aren't
on nuget.org. Download them into a folder (for example `packages/` next to the solution) and add it as a package
source in the solution's `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="webForCore" value="packages" />
  </packageSources>
</configuration>
```

Then reference `webForCore` (System.Web and the assemblies every app needs, plus the build targets) and the packages
for the frameworks the app uses (section 3). The package route needs no `<Import>` of `webForCore.WebStack.targets`:
`webForCore.Web` brings it.

**From source**, to work on webForCore itself or use unreleased changes: the app references webForCore's projects.

```bash
git clone https://github.com/jabba2324/webForCore
cd webForCore
dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build WebFormsForCore.slnx -c Release
```

You need the .NET SDK pinned in `global.json`. On Fedora and RHEL, also set `OPENSSL_ENABLE_SHA1_SIGNATURES=1`
(strong-name signing uses SHA-1).

## 2. Convert the project

Convert the web project to an SDK-style project, for example with `dotnet tool install -g upgrade-assistant`, or by
hand, and keep a copy of the old `.csproj`. Then use `Microsoft.NET.Sdk.Web` with these properties:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <!-- The output must be a direct subfolder of the site, as bin was on IIS. -->
    <OutputPath>bin_dotnet</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <GenerateDependencyFile>true</GenerateDependencyFile>
    <NoWarn>$(NoWarn);NU1701</NoWarn>
    <!-- Unknown culture names throw, as on .NET Framework. -->
    <PredefinedCulturesOnly>true</PredefinedCulturesOnly>
    <!-- MVC 5 and Web Pages: .cshtml files are compiled at run time by System.Web, not by ASP.NET Core's Razor SDK. -->
    <EnableDefaultRazorGenerateItems>false</EnableDefaultRazorGenerateItems>
    <RazorCompileOnBuild>false</RazorCompileOnBuild>
    <RazorCompileOnPublish>false</RazorCompileOnPublish>
  </PropertyGroup>

  <!-- Fixes the shared framework's System.Web facade conflict; required with project references (the packages bring
       it themselves). -->
  <Import Project="..\webForCore\src\webForCore.WebStack.targets" />

  <ItemGroup>
    <!-- App_Code is compiled at run time, as on IIS. Code-behind (CodeBehind=) stays in the project. -->
    <Compile Remove="App_Code\**" />
    <Compile Remove="bin_dotnet\**;obj\**" />
    <Content Remove="bin_dotnet\**" />
    <None Remove="bin_dotnet\**" />
  </ItemGroup>
</Project>
```

Pages that use `CodeBehind=` are compiled into the project's assembly, as Visual Studio did. Pages that use
`CodeFile=` are compiled at run time, as on IIS. Remove the old `bin` folder, `packages.config` and the
`Microsoft.CodeDom.Providers.DotNetCompilerPlatform` settings; webForCore brings its own compiler.

## 3. References

Remove the .NET Framework references and Microsoft's ASP.NET NuGet packages. With the packages, add
`<PackageReference Include="webForCore" Version="0.1.0" />` (the "Always" set below) and, for the frameworks the app
uses, the package of the same name with the `src/` folder's `webForCore.System.`/`webForCore.` prefix replaced by
`webForCore.` (`webForCore.Web.Mvc`, `webForCore.Web.Http.WebHost`, `webForCore.Microsoft.AspNet.SignalR.SystemWeb`,
`webForCore.Microsoft.Owin.Security.Cookies`, `webForCore.Web.Optimization`, `webForCore.AjaxControlToolkit`, ...).
From source, add project references to the fork. Everything an app needs:

| Always | Project (under `webForCore/src/`) |
| --- | --- |
| System.Web | `WebFormsForCore.Web/WebFormsForCore.Web.csproj` |
| System.Configuration | `WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj` |
| System.Web.ApplicationServices | `WebFormsForCore.Web.ApplicationServices/WebFormsForCore.Web.ApplicationServices.csproj` |
| System.Web.Services, System.Web.Extensions (listed by machine.config) | `WebFormsForCore.Web.Services/...`, `WebFormsForCore.Web.Extensions/...` |
| Microsoft.Web.Infrastructure | `WebFormsForCore.Web.Infrastructure/...` |
| System.Web.RegularExpressions | `WebFormsForCore.Web.RegularExpressions/...` |
| BinaryFormatter port (session, view state) | `WebFormsForCore.Serialization.Formatters/...` |
| System.Drawing | `WebFormsForCore.Drawing/...` |
| Run-time compiler | `WebFormsForCore.Compilers/...` |
| System.Web.DynamicData (data controls with `ItemType` load it) | `WebFormsForCore.Web.DynamicData/...` |

| If the app uses | Add |
| --- | --- |
| MVC 5 | `webForCore.System.Web.Mvc` (brings Razor and Web Pages); `webForCore.Microsoft.Web.Mvc` for MVC Futures |
| Web Pages (`.cshtml` without MVC) | `webForCore.System.Web.WebPages.Razor`, `webForCore.System.Web.WebPages.Deployment` |
| Helpers (WebGrid, Chart, WebImage) | `webForCore.System.Web.Helpers` |
| Web API 2 | `webForCore.System.Web.Http.WebHost` (and `webForCore.System.Web.Http.Cors` for CORS) |
| Web API on OWIN | `webForCore.System.Web.Http.Owin`, `webForCore.Microsoft.Owin.Host.SystemWeb` |
| OWIN security | `webForCore.Microsoft.Owin.Security.Cookies`, `.OAuth` |
| SignalR 2 | `webForCore.Microsoft.AspNet.SignalR.SystemWeb` |
| ASP.NET Identity 2 | `webForCore.Microsoft.AspNet.Identity.Owin`, `webForCore.Microsoft.AspNet.Identity.EntityFramework` |
| Bundling | `WebFormsForCore.Web.Optimization` (+ `.Optimization.WebForms` for `<webopt:BundleReference>`) |
| Chart control | `WebFormsForCore.Web.DataVisualization` |
| AjaxControlToolkit | `WebFormsForCore.AjaxControlToolkit/AjaxControlToolkit/AjaxControlToolkit.csproj` |

Keep other NuGet packages, preferring versions that target .NET Standard or .NET. The `test/webForCore.*.Sample`
projects in this repository are working examples for Web Forms, MVC, Web API and Web Pages.

## 4. Program.cs

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using WebFormsForCore.Hosting.IIS;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddWebForms();                 // IIS compression, W3C logging, systemd
        builder.Services.AddDistributedMemoryCache();   // session state (or Redis, SQL Server, ...)
        builder.Services.AddSession();

        var app = builder.Build();
        app.UseSession();
        app.UseWebForms(o => o
            .HandleAllRequestsWithWebForms()            // routing, extensionless URLs, static files through IIS rules
            .UseAspNetCoreSessionProvider());
        app.Run();
    }
}
```

Without `HandleAllRequestsWithWebForms()`, only ASP.NET's own extensions (`.aspx`, `.ashx`, `.asmx`, `.axd`, ...)
reach System.Web, and ASP.NET Core serves everything else.

## 5. Configuration

- **Keep `web.config`**, including `<system.webServer>`: webForCore reads its modules, handlers, rewrite rules,
  request filtering, default documents, custom errors and static content settings. See
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md) for what's covered.
- **Set an explicit `<machineKey>`** (validation and decryption keys). Generated keys change with every process, so
  forms authentication cookies, view state and anti-forgery tokens wouldn't survive a restart or work across servers.
- **Connection strings:** `System.Data.SqlClient` is used, as on .NET Framework. Add `TrustServerCertificate=True` or
  a trusted certificate if the server's certificate isn't trusted on Linux. LocalDB doesn't exist on Linux.
- **Protected sections:** sections encrypted with RSA can be read once the key container is imported
  (`webforcore-regiis -pi`). Sections encrypted with Windows DPAPI must be decrypted on Windows first. See
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#deployment-tools).
- **`<system.data><DbProviderFactories>`** entries are registered from configuration at application start, as on
  .NET Framework; the provider's assembly must be in `bin_dotnet`.
- **`<system.net><mailSettings>`** is used by ASP.NET's own mail senders (PasswordRecovery, CreateUserWizard,
  ChangePassword, `SmtpMail`, `WebMail`, health monitoring's `MailWebEventProvider`). .NET's `SmtpClient` and
  `MailMessage` don't read it: replace the application's `new SmtpClient()` with
  `WebFormsForCore.Net.Configuration.NetConfiguration.CreateSmtpClient()`, and set `From` from
  `NetConfiguration.DefaultFrom` where the code relied on `<smtp from>`.
- **`<system.net><defaultProxy>`** is applied at application start to `HttpClient.DefaultProxy`. Without it, .NET
  uses `HTTP_PROXY`, `HTTPS_PROXY` and `NO_PROXY`. `<connectionManagement>` and the other `<system.net>` sections
  have no effect.
- **Typed DataSets** (`.xsd` in App_Code) and their TableAdapters are generated when the site compiles. The `.xsd`'s
  provider must be registered; a TableAdapter's connection string comes from `<connectionStrings>`, as on IIS.

## 6. Code changes

Fix what the blocker search found (see [NAMESPACES.md](NAMESPACES.md) for replacements), then the Linux issues:

- **File paths:** Linux paths are case-sensitive and use `/`. Fix string-built paths (`"~\\Images\\Logo.PNG"`,
  `Path.Combine` with `\`) and file names whose case differs from the references to them. `Server.MapPath` and
  virtual paths work as before.
- **Newlines:** `Environment.NewLine` is `\n`.
- **Fonts and drawing:** install the fonts the app uses, or fontconfig's metric-compatible substitutes for Windows
  fonts (Liberation, Carlito, Caladea).
- **Culture data:** ICU's formats differ slightly from Windows NLS (for example some currency and date patterns).
  Check pages that format numbers and dates.
- **BinaryFormatter:** code that calls `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter` directly
  should use `WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter`.

## 7. Run and compare

```bash
dotnet build -c Release
dotnet bin_dotnet/YourApp.dll --urls http://localhost:5000     # from the site folder
```

The first request compiles pages, as on IIS. Then compare the responses recorded in step 3 of "Before you start"
(status codes, headers, bodies), and run the app's own tests. Also check that `/bin_dotnet/`, `/App_Data/`, `.cs` and
`.config` files return 404.

## 8. Deploy

- **One process per app.** Deploy the site folder (`web.config` at the top, the build in `bin_dotnet/`) and run it
  under systemd with `tools/deploy/webforcore@.service`. It maps the application pool's settings: identity, rapid-fail
  protection, ping, shutdown time and memory limits. A container works the same way.
- **In front:** a reverse proxy (nginx, Apache, a load balancer) for TLS and the public port, or Kestrel's own HTTPS.
- **Recycling:** a `web.config`, `bin` or `App_Code` change restarts the app in-process, as IIS did. Set time,
  request or memory recycling with `UseWebForms(o => o.Recycling(...))`.
- **Precompilation** (optional): `aspnetcore_compiler -v / -p <site> -b bin_dotnet [-u] <target>`, then copy the build
  into `<target>/bin_dotnet`. Use `-u` for updatable sites.
- **Scaling out:** use out-of-process session state (Redis or SQL Server through ASP.NET Core's distributed cache) and
  the same `<machineKey>` everywhere.
- Several processes must not share one site folder: they share the compilation folder.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `Could not load file or assembly 'System.Web'... conflicts with what is already bound` | `webForCore.WebStack.targets` isn't imported |
| `.cshtml` build errors from the ASP.NET Core Razor SDK | Razor SDK compilation isn't turned off (step 2) |
| `Type 'System.Web.Script.Services...' not found`, ScriptModule errors | System.Web.Extensions or System.Web.Services isn't referenced |
| 404 for files that exist | Case mismatch in the path, or a request filtering rule (`<hiddenSegments>`, `<fileExtensions>`) |
| Logins lost after a restart | No explicit `<machineKey>` |
| `PlatformNotSupportedException` | A Windows-only API: see [NAMESPACES.md](NAMESPACES.md) |
| 503 right after a deploy | Another process is compiling the same site folder |
