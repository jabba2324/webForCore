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
     `Microsoft.Owin.*`, `.SignalR.*`, `.Identity.*`, `Microsoft.AspNet.Web.Optimization`) are
     replaced by webForCore's projects. webForCore ships only Microsoft assemblies: WebActivatorEx and
     AjaxControlToolkit aren't replaced (below). Each is one version: MVC 5.3, Web API 5.3, Web Pages and Razor 3.3,
     Katana 4.2.3, SignalR 2.4.3, Identity 2.2. An application on an older version gets the upgrade with the
     migration, and the types the newer version added can be ambiguous with the application's own (#17): the Identity
     2.0 samples define `SignInStatus`, which Identity 2.1 added to `Microsoft.AspNet.Identity.Owin`, so a file with
     both namespaces gets `CS0104: 'SignInStatus' is an ambiguous reference` (OpenOrderFramework). Keep the
     application's type with an alias in that file:
     `using SignInStatus = OpenOrderFramework.Models.SignInStatus;`.
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

Two differences from .NET Framework projects come up when converting:

- **A `.resx` with non-string resources** (images, icons) fails with `MSB3822: Non-string resources require the
  System.Resources.Extensions assembly at runtime` (#36, Subtext). Add
  `<GenerateResourceUsePreserializedResources>true</GenerateResourceUsePreserializedResources>` and a reference to the
  `System.Resources.Extensions` package.
- **The C# version.** .NET Framework projects compiled with the C# of their time: 7.3 by default, or what "latest" meant
  then. On net11.0 the default is the newest, and C# 14's first-class span conversions resolve some calls differently
  (`CS0029: Cannot implicitly convert type 'System.ReadOnlySpan<char>' to 'string'`, YAF.NET's ServiceStack.Text). Keep
  the project's: `<LangVersion>7.3</LangVersion>` if it set none, or 13 for "latest". C# 11's ref-safety rules apply on
  .NET 7 and later whatever the version (`CS8347 ... may expose variables referenced by parameter 'index'`): mark such a
  `ref` parameter `scoped` (#43).
- **Project references are transitive.** A project now sees the packages and projects its references use.
  - Packages pinned at different versions in different projects fail with `NU1605: Detected package downgrade`.
    Use the highest version everywhere (#39, FunnelWeb's Iesi.Collections).
  - A namespace from an indirect reference can hide a type: `CS0118: 'X' is a namespace but is used like a type`. Put
    `PrivateAssets="all"` on the `ProjectReference` whose types its dependants shouldn't see (#39, mojoPortal's data
    layer).

## 3. References

Remove the .NET Framework references and Microsoft's ASP.NET NuGet packages. With the packages, add
`<PackageReference Include="webForCore" Version="0.5.0" />` (the "Always" set below) and, for the frameworks the app
uses, the package of the same name with the `src/` folder's `webForCore.System.`/`webForCore.` prefix replaced by
`webForCore.` (`webForCore.Web.Mvc`, `webForCore.Web.Http.WebHost`, `webForCore.Microsoft.AspNet.SignalR.SystemWeb`,
`webForCore.Microsoft.Owin.Security.Cookies`, `webForCore.Web.Optimization`, ...).
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

webForCore ships only Microsoft assemblies, so two libraries apps commonly have need attention:

- **WebActivatorEx** (Unity.Mvc, Ninject.MVC, StructureMap.MVC and others bring it): its start methods never run on
  webForCore. It finds its attributes with `Assembly.LoadFrom`, which on .NET loads second copies of the application's
  assemblies outside the application (#21; [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md)). Keep the package, and call
  each `[assembly: WebActivatorEx.PreApplicationStartMethod(typeof(X), "Start")]` method from `Application_Start` in
  Global.asax (`X.Start()`), post-start methods after it, and shutdown methods from `Application_End`. WebActivator 1.x
  (`[assembly: WebActivator.PreApplicationStartMethod]`, which MVC 3 era packages such as Ninject.MVC3 bring) is the
  same (#38, Subtext).
- **AjaxControlToolkit** isn't provided. The original package is built for .NET Framework.

Keep other NuGet packages, preferring versions that target .NET Standard or .NET. The `test/webForCore.*.Sample`
projects in this repository are working examples for Web Forms, MVC, Web API and Web Pages.

### Packages that need more than a reference

These came up migrating real applications; each names one that needed it.

- **Packages built on MVC, Web API or OWIN bring Microsoft's packages back** (#4, #23). `Autofac.Mvc5`, `Autofac.WebApi2`,
  `Unity.Mvc`, Ninject's and Glimpse's MVC packages, Swashbuckle, `Microsoft.Owin.Security.WsFederation` and others
  depend on `Microsoft.AspNet.Mvc`, `Microsoft.AspNet.WebApi.Core`, `Microsoft.Owin` and so on, and NuGet restores
  those, with assemblies of the same names as webForCore's. With webForCore's packages nothing needs doing: its build
  targets make its packages override Microsoft's in the SDK's conflict resolution (before 0.3.0, Microsoft's
  `System.Web.Razor` and `System.Web.WebPages` won on file version, and MVC views failed with a `MissingMethodException`:
  Bonobo Git Server, ApplicantTracking, ReaLocate, Roadkill). An
  application built against webForCore's projects instead (a source build) gets both, so the compiler sees two
  `System.Web.Mvc`s: `CS0433: The type 'Controller' exists in both 'System.Web.Mvc, Version=5.1.0.0' and
  'System.Web.Mvc, Version=5.3.1.0'`. There, reference each one that `obj/project.assets.json` lists, at the version
  restored, with no assets, so the direct reference wins and contributes nothing (eShopLegacyMVC):
  ```xml
  <PackageReference Include="Microsoft.AspNet.Mvc" Version="5.1.0" ExcludeAssets="all" />
  <PackageReference Include="Microsoft.AspNet.WebApi.Core" Version="5.2.0" ExcludeAssets="all" />
  ```
- **A `lib/net45`-only package with a `build/` folder restores without its assembly** (#5). NuGet counts `build/`
  as compatible, so `AssetTargetFallback` doesn't apply, and the DLL isn't copied: `Could not load file or assembly
  'Microsoft.AI.Web'` for a module web.config names (Application Insights 2.x in eShopLegacyMVC). Reference the DLL
  from the package (packages with only `lib/net45`, such as `Autofac.Mvc5`, restore normally):
  ```xml
  <PackageReference Include="Microsoft.ApplicationInsights.Web" Version="2.9.1" GeneratePathProperty="true" />
  <Reference Include="Microsoft.AI.Web" HintPath="$(PkgMicrosoft_ApplicationInsights_Web)/lib/net45/Microsoft.AI.Web.dll" />
  ```
- **Versions webForCore requires** (#6). Restoring fails with `NU1605: Detected package downgrade` below these:
  `Newtonsoft.Json` 13.0.4 (Web API's formatters, bundling), `Microsoft.IdentityModel.Tokens` 8.19.1 (through
  System.Web.Extensions' CoreWCF client support) and `Humanizer.Core` 2.14.1 (through Roslyn in `webForCore.Compilers`;
  aspnet-mvc5-starter had 2.6.2, #30). `System.Configuration.ConfigurationManager` and `System.Runtime.Caching` come
  with webForCore at 11.0: remove the application's references to them (MVCForum had 4.5 previews). More generally, every
  package webForCore depends on is a floor for the application's reference to it: `System.Data.SqlClient` 4.9.1 (YAF.NET's
  ServiceStack.OrmLite.SqlServer had 4.8.1) and the System.* packages at 11.0, such as `System.DirectoryServices`
  (Opserver had 4.5.0) (#42). Raise the application's reference, or remove it if the package was
  only there as another package's dependency. `EntityFramework` before 6.4 has no .NET build: use 6.4 or later
  (6.5.1 works with `System.Data.SqlClient`).
- **.NET Framework assemblies that are packages on .NET** (#9). The root web.config of .NET Framework referenced its
  framework assemblies for every page, so App_Code and pages used them freely. On .NET some are NuGet packages the
  application must reference, or code compiled at run time fails with `CS1069: The type name 'SyndicationItem' could not
  be found ... forwarded to assembly 'System.ServiceModel.Syndication'` (MiniBlog's feed handler). Among them:
  `System.ServiceModel.Syndication`, `System.Data.SqlClient`, `System.Data.OleDb`, `System.DirectoryServices`,
  `System.Runtime.Caching`, `System.ServiceModel.Http` and the other WCF client packages.
- **A library's own types named like System.Web.ApplicationServices' become ambiguous** (#29). On .NET Framework a
  library that references only `System.Web` doesn't see the membership types (`MembershipUser`, `MembershipCreateStatus`,
  `MembershipProvider`, `Roles`, ...), which `System.Web` forwards to `System.Web.ApplicationServices`. The `webForCore`
  package references `System.Web.ApplicationServices` for every project, so a library that defines its own
  `MembershipUser` and has `using System.Web.Security;` gets `CS0104: 'MembershipUser' is an ambiguous reference`
  (MVCForum). Keep it out of that library's compilation, as it was:
  ```xml
  <PackageReference Include="webForCore.Web.ApplicationServices" Version="0.5.0" ExcludeAssets="compile" />
  ```
- **A package's .NET Standard build can have less than its `net45` build** (#31). NuGet picks the .NET Standard build
  for .NET 11, and some packages built it with a smaller API. WindowsAzure.Storage 9.x has no synchronous blob methods
  there (`Exists`, `OpenRead`, ...: aspnet-mvc5-starter). Hangfire 1.6's has no OWIN extensions (`app.UseHangfireServer`),
  and its SqlServer storage can't look a connection string name up in web.config (MVCForum). Use the version's `net45`
  build, in every project that gets the package, the web project included:
  ```xml
  <PackageReference Include="Hangfire.Core" Version="1.6.17" GeneratePathProperty="true" ExcludeAssets="compile;runtime" />
  <Reference Include="Hangfire.Core" HintPath="$(PkgHangfire_Core)/lib/net45/Hangfire.Core.dll" />
  ```
- **Katana packages webForCore doesn't lift** (#14, #18). webForCore provides `Microsoft.Owin`, its hosts, `Security`,
  `Security.Cookies`, `Security.OAuth` and `Cors`. The social logins of the Visual Studio templates
  (`Microsoft.Owin.Security.Facebook`, `.Google`, `.MicrosoftAccount`, `.Twitter`) and `.WsFederation` are not: keep
  the application's package (any version from 2.0). Its Katana dependencies lose to webForCore's `Microsoft.Owin`
  4.2.3, and the `System.Net.Http.WebRequest` they create their back-channel handler from is in the "Always" set
  (before 0.3.0 it wasn't, and registering a provider failed every request: OpenOrderFramework, SocialGoal). Every
  template app has `using Microsoft.Owin.Security.Google;` in `Startup.Auth.cs`, even with the providers commented out
  (MvcMovie, Bonobo Git Server).
  ```xml
  <PackageReference Include="Microsoft.Owin.Security.Google" Version="3.0.1" />
  ```
- **Libraries that don't run on .NET** (#22). Remove them or move to a version that does:
  - **Glimpse 1.x** (`Glimpse`, `.AspNet`, `.Ado`, `.EF6`, `.Mvc5`, ...), a development diagnostics tool, can't run:
    Glimpse.Ado's pre-start method reflects into .NET Framework's private `DbProviderFactories` table
    (`NullReferenceException` in `FindDbProviderFactoryTable`), and `Glimpse.AspNet.HttpModule` needs
    `AppDomainSetup.ShadowCopyFiles` (`MissingMethodException`). Remove the packages, the `glimpse` configuration
    section, its module and handler in `<system.web>` and `<system.webServer>`, and the app's `GlimpseSecurityPolicy`
    class (ReaLocate).
  - **AutoMapper before 12** throws while configuring a map on .NET 11 (`The type or method has 3 generic parameter(s),
    but 1 generic argument(s) were provided`, or `... violates the constraint of type 'T'` from `MaxFloat`/`MaxInteger`):
    it scans `System.Linq.Enumerable`'s extension methods, non-public ones included. This isn't webForCore's: a .NET 11
    console app does the same. AutoMapper 12 works; from 4.x the change is mostly `IMapperConfiguration` →
    `IMapperConfigurationExpression` (ReaLocate). From 3.x's static `Mapper` it is a rewrite of the mapping code.

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
- **Remove `<compilation><assemblies>` entries for assemblies .NET doesn't have**, such as System.Design and
  System.Data.Linq ([NAMESPACES.md](NAMESPACES.md)). Each one is a Configuration Error on every page ("Could not load
  file or assembly 'System.Design, Version=4.0.0.0'"), even when no page uses it (#37, Subtext and mojoPortal).
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
- **The site's folder:** `AppDomain.CurrentDomain.BaseDirectory` (and `AppContext.BaseDirectory`) is the build output,
  `bin_dotnet/`, not the site root as in IIS's per-application AppDomain. Find the site's files with
  `HttpRuntime.AppDomainAppPath`, `HostingEnvironment.ApplicationPhysicalPath` or `Server.MapPath("~/...")` instead
  ([NAMESPACES.md](NAMESPACES.md)). Search for `BaseDirectory` and `ApplicationBase`: eShopLegacyMVC's database seed read
  its SQL scripts that way.
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
