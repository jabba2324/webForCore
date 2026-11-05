# Migrating an ASP.NET 4.x app to webForCore

This guide takes an existing Web Forms, MVC 5, Web API 2 or Web Pages application and runs it on .NET 10 with
webForCore, on Linux or Windows. The approach keeps the application's code, markup and `web.config`: the project
becomes an SDK-style .NET 10 app whose `Program.cs` hosts the same ASP.NET pipeline on Kestrel.

For an AI agent doing the migration, the same procedure is written as instructions in
[AGENT-MIGRATION.md](AGENT-MIGRATION.md).

## Before you start

1. **Check the blockers.** Search the solution for the APIs in the summary table of [NAMESPACES.md](NAMESPACES.md):
   `WindowsIdentity`, `<authentication mode="Windows">`, `System.ServiceModel` hosting (`.svc`), `LinqDataSource`,
   `ObjectContext`/EDMX (EF 1–4), registry and COM use, `App_WebReferences`,
   EMF/WMF metafiles, Windows Identity Foundation (`System.IdentityModel`, `<system.identityModel>`,
   `WSFederationAuthenticationModule`), Windows Workflow Foundation (`System.Workflow`, `System.Activities`) and WCF
   Data Services hosting (`System.Data.Services`, `DataService<T>`). Each one needs a code change; plan these first.
   A **Web Site Project** (no `.csproj`, pages with `CodeFile=`) converts differently: see section 2.
2. **Inventory the dependencies.** List NuGet packages and referenced DLLs:
   - Microsoft's ASP.NET packages (`Microsoft.AspNet.Mvc`, `.WebApi.*`, `.WebPages`, `.Razor`, `.Cors`,
     `Microsoft.Owin.*`, `.SignalR.*`, `.Identity.*`, `Microsoft.AspNet.Web.Optimization`) are
     replaced by webForCore's projects. webForCore ships only Microsoft assemblies: WebActivatorEx runs as it is, and
     AjaxControlToolkit isn't replaced (below). Each is one version: MVC 5.3, Web API 5.3, Web Pages and Razor 3.3,
     Katana 4.2.3, SignalR 2.4.3, Identity 2.2. An application on an older version gets the upgrade with the
     migration, and the types the newer version added can be ambiguous with the application's own (#17): the Identity
     2.0 samples define `SignInStatus`, which Identity 2.1 added to `Microsoft.AspNet.Identity.Owin`, so a file with
     both namespaces gets `CS0104: 'SignInStatus' is an ambiguous reference` (OpenOrderFramework). Keep the
     application's type with an alias in that file:
     `using SignInStatus = OpenOrderFramework.Models.SignInStatus;`.
     An **MVC 3 or 4** application needs two more changes, as Microsoft's own upgrade to MVC 5 did (#122, MVC Music
     Store):
     - `<add key="webpages:Version" value="1.0.0.0"/>` (MVC 4: `2.0.0.0`) makes every request a 500: `Conflicting
       versions of ASP.NET Web Pages detected: specified version is "1.0.0.0", but the version in bin is "3.0.0.0"`.
       Set it to `3.0.0.0`, or remove the key. The other old version numbers (`<compilation><assemblies>`,
       `Views/Web.config`'s Razor section types) need no change: .NET binds them to the newer assemblies.
     - The templates' `AccountModels.cs` has `using System.ComponentModel.DataAnnotations;` and
       `using System.Web.Mvc;`, and .NET Framework 4.5 added a `CompareAttribute` to DataAnnotations: `CS0104: 'Compare'
       is an ambiguous reference between 'System.ComponentModel.DataAnnotations.CompareAttribute' and
       'System.Web.Mvc.CompareAttribute'`. Keep MVC's, which it compiled to on 4.0 (and which keeps the client
       validation): `using CompareAttribute = System.Web.Mvc.CompareAttribute;`.

     Views written for Razor 1 (MVC 3, Web Pages 1 and WebMatrix sites) can fail to parse on Razor 3, the only Razor
     webForCore has (#174): Razor 2 and 3 refuse `@` before a C# keyword inside a code block (`@string.Join(...)`,
     `@int.Parse(...)`), `Parser Error Message: Unexpected "string" keyword after "@" character. Once inside code, you
     do not need to prefix constructs like "string" with "@".` Razor 1 accepted it (rabbit-webpages, a WebMatrix 1.0
     site, shipped its page editor so). Wrap the expression: `@(string.Join(", ", Page.Tags))`.
   - Pure managed libraries that target .NET Standard or .NET usually work as they are. Newtonsoft.Json, Autofac,
     EntityFramework 6.4+ and log4net are examples.
   - Libraries built only for .NET Framework that reference System.Web often load, because webForCore's assemblies
     carry the same names. Test each one. Closed-source control suites need checking with the vendor.
3. **Record the reference behaviour.** With the app running on IIS, capture the responses of its main pages and APIs:
   status, headers and body. You'll compare against these later.
4. **Set `targetFramework`.** `<httpRuntime targetFramework="4.5">` or later is the behaviour webForCore reproduces.
   If the app has no `targetFramework`, add one and test on IIS first.

## 1. Get webForCore

**From the packages (simplest).** webForCore's NuGet packages aren't on nuget.org. From 0.7.0 they are on a NuGet
feed on GitHub Pages, and System.Drawing comes from [drawingForCore](https://github.com/xforcore/drawingForCore)'s
feed, which the `webForCore` package depends on. Add both as package sources in the solution's `nuget.config`:

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="webForCore" value="https://xforcore.github.io/webForCore/index.json" />
    <add key="drawingForCore" value="https://xforcore.github.io/drawingForCore/index.json" />
  </packageSources>
</configuration>
```

The feed keeps the latest five releases. Every release's packages are also attached to it on GitHub
([Releases](https://github.com/xforcore/webForCore/releases)); 0.6.0 and earlier are only there (download them into a
folder and add the folder as a package source; they include System.Drawing as `webForCore.Drawing`).

Then reference `webForCore` (System.Web and the assemblies every app needs, plus the build targets) and the packages
for the frameworks the app uses (section 3). The package route needs no `<Import>` of `webForCore.WebStack.targets`:
`webForCore.Web` brings it.

**From source**, to work on webForCore itself or use unreleased changes: the app references webForCore's projects.

```bash
git clone https://github.com/xforcore/webForCore
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
    <TargetFramework>net10.0</TargetFramework>
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
`Microsoft.CodeDom.Providers.DotNetCompilerPlatform` settings; webForCore brings its own compiler. `<system.codedom>`
entries that name .NET Framework's own `Microsoft.CSharp.CSharpCodeProvider` or `Microsoft.VisualBasic.VBCodeProvider` (in
`System`, as the .NET 3.5 and 4.0 project templates wrote them) can stay: they mean webForCore's compilers, with the
entry's warning level, compiler options and provider options (`WarnAsError`, `OptionInfer`), as on IIS (#88, Umbraco
6.2.6). Their `CompilerVersion` doesn't pick an older compiler.

These differences from .NET Framework projects come up when converting:

- **A `.resx` with non-string resources** (images, icons) fails with `MSB3822: Non-string resources require the
  System.Resources.Extensions assembly at runtime` (#36, Subtext). Add
  `<GenerateResourceUsePreserializedResources>true</GenerateResourceUsePreserializedResources>` and a reference to the
  `System.Resources.Extensions` package.
- **The C# version.** .NET Framework projects compiled with the C# of their time: 7.3 by default, or what "latest" meant
  then. On net10.0 the default is the newest, and C# 14's first-class span conversions resolve some calls differently
  (`CS0029: Cannot implicitly convert type 'System.ReadOnlySpan<char>' to 'string'`, YAF.NET's ServiceStack.Text). A
  project that compiles can still fail at run time: in an expression tree, `ids.Contains(p.Id)` on an array binds to
  `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` instead of `Enumerable.Contains`, which EF 6 and other `IQueryable`
  providers can't translate: ``NotSupportedException: LINQ to Entities does not recognize the method 'Boolean
  Contains[Int32](System.ReadOnlySpan`1[System.Int32], Int32)' method`` (#138, nopCommerce 3.90's product pages). Keep
  the project's: `<LangVersion>7.3</LangVersion>` if it set none, or 13 for "latest". C# 11's ref-safety rules apply on
  .NET 7 and later whatever the version (`CS8347 ... may expose variables referenced by parameter 'index'`): mark such a
  `ref` parameter `scoped` (#43). An application that references CoreWCF itself (a CoreWCF service) gets CoreWCF's source
  generator, which needs C# 11 for every `[ServiceContract]` (`CS8370: Feature 'file types' is not available`): set
  `<EnableCoreWCFOperationInvokerGenerator>false</EnableCoreWCFOperationInvokerGenerator>` with an older `LangVersion`.
  webForCore's own CoreWCF dependency doesn't bring it (#134, C1 CMS).
- **Calls that become ambiguous** (`CS0121: The call is ambiguous between ...`, #93). `<LangVersion>` doesn't help
  with either of these; qualify the call:
  - *The application's own LINQ-style extension methods.* .NET 6 added `DistinctBy`, `MinBy`, `MaxBy`, `Chunk`,
    `ExceptBy`, `IntersectBy`, `UnionBy` and others to `System.Linq.Enumerable`. An application (or MoreLINQ) that has
    its own gets `CS0121 ... 'System.Linq.Enumerable.DistinctBy<TSource, TKey>(...)' and
    'Umbraco.Core.EnumerableExtensions.DistinctBy<T, TKey>(...)'` in every file that imports both namespaces (Umbraco
    6.2.6 and 7.15.11); code in the extension class's own namespace still binds to the application's. Call the
    application's method statically, around the whole chain before it (or remove the application's copy where it
    behaves the same):
    ```csharp
    // was: var propertyTypes = types.ToArray().SelectMany(x => x.PropertyTypes).DistinctBy(c => c.Alias);
    var propertyTypes = Umbraco.Core.EnumerableExtensions.DistinctBy(
        types.ToArray().SelectMany(x => x.PropertyTypes), c => c.Alias);
    ```
  - *A `params` call with no arguments.* Visual Studio 2013's and earlier compilers chose an overload that Roslyn finds
    ambiguous: Umbraco 6's `SqlHelper<P>` has `ExecuteReader(string, params IParameter[])` and
    `ExecuteReader(string, params P[])`, and its subclasses' `ExecuteReader("select ...")` is `CS0121`. Pass the empty
    array of the overload that was meant: `ExecuteReader("select ...", new IParameter[0])`.
- **Types .NET added that have the name of one of the application's** (#136). .NET Core 2.0 added a non-generic
  static `System.Collections.Generic.KeyValuePair` (`KeyValuePair.Create`), so an application's own non-generic
  `KeyValuePair` in a file with `using System.Collections.Generic;` gets `CS0104: 'KeyValuePair' is an ambiguous
  reference between 'Composite.Core.Types.KeyValuePair' and 'System.Collections.Generic.KeyValuePair'` (C1 CMS). Alias
  the application's type in that file: `using KeyValuePair = Composite.Core.Types.KeyValuePair;`. An alias has no type
  parameters, so `KeyValuePair<TKey, TValue>` still binds to .NET's.
- **Design-time classes don't compile** (#144). .NET has no System.Design, so an application's designer classes
  (subclasses of `System.Web.UI.Design.ExpressionEditor`, `ControlDesigner`, `DataSourceDesigner`, and System.Design's
  editors) fail: `CS0234: The type or namespace name 'Design' does not exist in the namespace 'System.Web.UI'` (DNN 9's
  `LocalizationExpressionBuilderEditor`). Nothing uses them at run time: remove them from the build
  (`<Compile Remove="...Editor.cs" />`). `[ExpressionEditor("...")]`, `[Designer("...")]` and `[Editor("...", ...)]`
  that name them by string keep working; one that names them with `typeof` needs the string form.
- **Project references are transitive.** A project now sees the packages and projects its references use.
  - Packages pinned at different versions in different projects fail with `NU1605: Detected package downgrade`.
    Use the highest version everywhere (#39, FunnelWeb's Iesi.Collections).
  - A namespace from an indirect reference can hide a type: `CS0118: 'X' is a namespace but is used like a type`. Put
    `PrivateAssets="all"` on the `ProjectReference` whose types its dependants shouldn't see (#39, mojoPortal's data
    layer).
- **Class libraries that reference webForCore** (#62). .NET 10 prunes the `Microsoft.Extensions.*` and
  `Microsoft.AspNetCore.*` packages (treats them as part of the framework) only in projects that reference the ASP.NET
  Core shared framework. The web project does, through `Microsoft.NET.Sdk.Web`. A `Microsoft.NET.Sdk` class library
  doesn't, even though the webForCore packages declare the framework. The library then gets the versions that
  webForCore's WCF client support brings (through CoreWCF.Http: `Microsoft.Extensions.*` 8.0 to 10.0,
  `Microsoft.AspNetCore.*` 2.3), and its own older references to them fail:
  `NU1605: Detected package downgrade: Microsoft.Extensions.Primitives from 8.0.0 to 5.0.1` (YAF.NET's Lucene.Net
  libraries). Add the framework to every class library that references webForCore (it runs on ASP.NET Core anyway),
  then remove the references that `NU1510: PackageReference ... will not be pruned` reports:
  ```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  ```
- **Plug-in libraries built into a folder of their own** (#150). Apps whose plug-ins are class libraries the web
  project doesn't reference, built to a folder the app loads them from (Orchard's `Modules/<id>/bin/`, nopCommerce's
  and SmartStoreNET's plug-in folders), relied on .NET Framework projects copying their references there, packages
  included. An SDK class library doesn't copy its packages' assemblies, so the plug-in fails when it's loaded:
  `FileNotFoundException: Could not load file or assembly 'Microsoft.Data.Services.Client, Version=5.8.4.0'`. Keep the
  old `OutputPath` (with `AppendTargetFrameworkToOutputPath` false), and copy only the plug-in's own packages
  (`CopyLocalLockFileAssemblies` alone copies webForCore's and the framework's too, a couple of hundred assemblies):
  ```xml
  <PropertyGroup>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <PluginPackages>;Orchard.NuGet.Core;Microsoft.Data.Services.Client;Microsoft.Data.OData;Microsoft.Data.Edm;System.Spatial;</PluginPackages>
  </PropertyGroup>
  <Target Name="KeepOnlyPluginPackages" AfterTargets="ResolveLockFileCopyLocalFiles">
    <ItemGroup>
      <ReferenceCopyLocalPaths Remove="@(ReferenceCopyLocalPaths)" Condition="'%(ReferenceCopyLocalPaths.NuGetPackageId)' != '' and !$(PluginPackages.Contains(';%(ReferenceCopyLocalPaths.NuGetPackageId);'))" />
    </ItemGroup>
  </Target>
  ```
  Put `Private="false"` on the plug-in's `ProjectReference`s to the host's own libraries, as the old project had, so
  they aren't copied either.
- **VB projects** (#121). A `.vbproj` converts the same way (`Microsoft.NET.Sdk.Web`, or `Microsoft.NET.Sdk` for a
  library, with the same properties), with these differences:
  - `<MyType>Windows</MyType>`, the default of Visual Studio's VB class library templates, fails in the `My` code the
    compiler generates: `BC30002: Type 'Global.Microsoft.VisualBasic.ApplicationServices.ApplicationBase' is not
    defined` (and `...ApplicationServices.User`, `...Devices.Computer`). On .NET those types are in Windows Desktop's
    `Microsoft.VisualBasic.Forms`. Use `<MyType>Empty</MyType>`; code that used `My.Computer`, `My.User` or
    `My.Application` needs a change (`My.Computer.FileSystem` → `System.IO`). (SignalR 2.4.3's sample's VB hub library.)
  - Keep the project's `<Import Include="System.Web" />` namespace items: VB imports them for every file.
  - Set `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` when `My Project/AssemblyInfo.vb` exists.
  - A VB class library gets the `FrameworkReference` above, as a C# one does.

### A Web Site Project (no `.csproj`)

A Visual Studio Web Site Project has no project file (the solution lists the folder as a web site), its pages and
controls use `CodeFile=`, its other code is in `App_Code`, and `Bin` holds `*.dll.refresh` files and the assemblies
other projects copy there (#131, Rock RMS's `RockWeb`). webForCore compiles all of it at run time, as IIS did. Create
the SDK project in the site's folder, compiling only `Program.cs`: the SDK's glob would compile the `CodeFile=` code
too, which fails (`CS0103: The name 'lbl' does not exist in the current context`).

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <OutputPath>bin_dotnet</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <GenerateDependencyFile>true</GenerateDependencyFile>
    <NoWarn>$(NoWarn);NU1701</NoWarn>
    <PredefinedCulturesOnly>true</PredefinedCulturesOnly>
    <EnableDefaultRazorGenerateItems>false</EnableDefaultRazorGenerateItems>
    <RazorCompileOnBuild>false</RazorCompileOnBuild>
    <RazorCompileOnPublish>false</RazorCompileOnPublish>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="webForCore" Version="0.9.0" />
  </ItemGroup>
  <ItemGroup>
    <!-- CodeFile= pages and controls, Global.asax and App_Code are compiled at run time, as on IIS. -->
    <Compile Remove="**\*.cs" />
    <Compile Include="Program.cs" />
    <Content Remove="bin_dotnet\**" />
    <None Remove="bin_dotnet\**" />
  </ItemGroup>
</Project>
```

- The references come from `Bin`: each `*.dll.refresh` names a package's DLL (a `PackageReference`), and the
  assemblies the solution's other projects copy into `Bin` become `ProjectReference`s. Remove those projects' copy
  targets (Rock's `CopyToRockWeb`): the assemblies must land in `bin_dotnet/`.
- Remove the old `Bin` folder's DLLs; the site's `Bin` isn't `bin_dotnet`.

## 3. References

Remove the .NET Framework references and Microsoft's ASP.NET NuGet packages. With the packages, add
`<PackageReference Include="webForCore" Version="0.9.0" />` (the "Always" set below) and, for the frameworks the app
uses, the package of the same name with the `src/` folder's `webForCore.System.`/`webForCore.` prefix replaced by
`webForCore.` (`webForCore.Web.Mvc`, `webForCore.Web.Http.WebHost`, `webForCore.Microsoft.AspNet.SignalR.SystemWeb`,
`webForCore.Microsoft.Owin.Security.Cookies`, `webForCore.Web.Optimization`, ...).
From source, add project references to the fork. System.Drawing then comes with System.Web, as drawingForCore's
packages, so the app's `nuget.config` needs drawingForCore's feed (above). Everything an app needs:

| Always | Project (under `webForCore/src/`) |
| --- | --- |
| System.Web | `WebFormsForCore.Web/WebFormsForCore.Web.csproj` |
| System.Configuration | `WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj` |
| System.Web.ApplicationServices | `WebFormsForCore.Web.ApplicationServices/WebFormsForCore.Web.ApplicationServices.csproj` |
| System.Web.Services, System.Web.Extensions (listed by machine.config) | `WebFormsForCore.Web.Services/...`, `WebFormsForCore.Web.Extensions/...` |
| Microsoft.Web.Infrastructure | `WebFormsForCore.Web.Infrastructure/...` |
| System.Web.RegularExpressions | `WebFormsForCore.Web.RegularExpressions/...` |
| BinaryFormatter port (session, view state) | `WebFormsForCore.Serialization.Formatters/...` |
| Run-time compiler | `WebFormsForCore.Compilers/...` |
| System.Web.DynamicData (data controls with `ItemType` load it) | `WebFormsForCore.Web.DynamicData/...` |
| LINQ to SQL (System.Data.Linq, in .NET Framework's GAC) | `webForCore.System.Data.Linq/...` |

| If the app uses | Add |
| --- | --- |
| MVC 5 | `webForCore.System.Web.Mvc` (brings Razor and Web Pages); `webForCore.Microsoft.Web.Mvc` for MVC Futures |
| Web Pages (`.cshtml` without MVC) | `webForCore.System.Web.WebPages.Razor`, `webForCore.System.Web.WebPages.Deployment` |
| Helpers (WebGrid, Chart, WebImage) | `webForCore.System.Web.Helpers` |
| Web API 2 | `webForCore.System.Web.Http.WebHost` (and `webForCore.System.Web.Http.Cors` for CORS) |
| Web API on OWIN | `webForCore.System.Web.Http.Owin`, `webForCore.Microsoft.Owin.Host.SystemWeb` |
| OWIN security | `webForCore.Microsoft.Owin.Security.Cookies`, `.OAuth`; `.OpenIdConnect` and `.Jwt` (Entra ID / Azure AD), `.WsFederation`, `.ActiveDirectory`, `.Google`, `.Facebook`, `.MicrosoftAccount`, `.Twitter` ([below](#packages-that-need-more-than-a-reference)) |
| SignalR 2 | `webForCore.Microsoft.AspNet.SignalR.SystemWeb`; for several instances, a backplane: `webForCore.Microsoft.AspNet.SignalR.StackExchangeRedis`, `.Redis` or `.SqlServer` ([below](#scaling-out-signalr)) |
| ASP.NET Identity 2 | `webForCore.Microsoft.AspNet.Identity.Owin`, `webForCore.Microsoft.AspNet.Identity.EntityFramework` |
| Bundling | `WebFormsForCore.Web.Optimization` (+ `.Optimization.WebForms` for `<webopt:BundleReference>`) |
| Chart control | `WebFormsForCore.Web.DataVisualization` |

webForCore ships only Microsoft assemblies. Two libraries apps commonly have:

- **WebActivatorEx** (Unity.Mvc, Ninject.MVC, StructureMap.MVC and others bring it) runs as it is, from 0.7.0: its
  pre-start and post-start methods in `bin` assemblies run as on IIS. Before 0.7.0 they never ran, because
  the application had its own load context and the `Assembly.LoadFrom` WebActivatorEx finds its attributes with
  returned second copies of the application's assemblies from .NET's default one (#21). Since workers (#56) the
  application is in the default context, so `LoadFrom` on a `bin` assembly returns the copy the application has.
  Checked on 0.7.0 with the original WebActivatorEx 2.2.0 and WebActivator 1.5.0 packages (WebActivator 1.x is what MVC
  3 era packages such as Ninject.MVC3 bring; #38, Subtext). As on IIS, a pre-start method in `App_Code` doesn't run:
  `App_Code` is compiled after the pre-start phase.
- **AjaxControlToolkit** isn't provided: the original package is built for .NET Framework.
  [webForCore-thirdparty](https://github.com/xforcore/webForCore-thirdparty) builds it for .NET 10 (it isn't part of
  webForCore).

**WebMatrix.Data and WebMatrix.WebData** (`Database.Open`, `db.Query`; `WebSecurity`, `SimpleMembershipProvider`,
`SimpleRoleProvider`) aren't built by webForCore (#130). Microsoft's packages run as they are; their dependencies on
Web Pages and Microsoft.Web.Infrastructure lose to webForCore's, as MVC packages' do:

```xml
<PackageReference Include="Microsoft.AspNet.WebPages.Data" Version="3.2.9" />
<PackageReference Include="Microsoft.AspNet.WebPages.WebData" Version="3.2.9" />
```

Every WebMatrix template site (Starter Site, Bakery, Photo Gallery) and the MVC 4 Internet Application template
(`InitializeSimpleMembership`) uses them. On WebMatrix sites they came from the GAC, so `bin` has nothing to keep: add
the packages. `WebSecurity.InitializeDatabaseConnection(..., autoCreateTables: true)`, registering, logging in and out
and changing the password work on SQL Server (the Starter Site), and WebData's pre-start method (SimpleMembership, forms
authentication) runs.

Keep other NuGet packages, preferring versions that target .NET Standard or .NET. The `test/webForCore.*.Sample`
projects in this repository are working examples for Web Forms, MVC, Web API and Web Pages. A .NET Framework package
built against MVC 5 runs as it is unless it uses an API .NET lacks: Microsoft's own MVC Futures 5.0.0 binary (built for
.NET Framework 4.0 against MVC 5.0) ran unmodified on webForCore's MVC 5.3, with .NET binding its 5.0.0.0 reference.

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
  'System.Web.Mvc, Version=5.3.0.0'`. There, reference each one that `obj/project.assets.json` lists, at the version
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
  package webForCore depends on is a floor for the application's reference to it: the System.* packages at 11.0, such
  as `System.DirectoryServices` (Opserver had 4.5.0) (#42). Raise the application's reference, or remove it if the
  package was only there as another package's dependency. `System.Data.SqlClient` isn't one of them: see the next
  item. `EntityFramework` before 6.4 has no
  .NET build: use 6.4 or later (6.5.1 works with `System.Data.SqlClient`, webForCore's or Microsoft's). EntityFramework 6.5.1 needs `System.ComponentModel.Annotations` 5.0.0,
  and .NET 10 treats only versions up to 4.3.0 as part of the framework, so a lower reference fails:
  `NU1605: Detected package downgrade: System.ComponentModel.Annotations from 5.0.0 to 4.4.1` (MVCForum). Raise it to
  5.0.0 in every project that has it, or remove it where the project doesn't use it directly (#62). The application's
  own Roslyn packages (`Microsoft.CodeAnalysis.*`: Umbraco 8's ModelsBuilder, scripting, analyzers referenced as
  packages) must be 5.9.0, `webForCore.Compilers`' version. Roslyn's packages pin each other exactly, so the error is
  `NU1107: Version conflict detected for Microsoft.CodeAnalysis.Common`, not `NU1605` (#153, Umbraco 8.18.15's
  `Microsoft.CodeAnalysis.CSharp` 2.10.0). Roslyn's API is compatible: raise the reference to 5.9.0.
- **`System.Data.SqlClient`: webForCore's, or Microsoft's supported package** (#41, #195). On .NET, System.Data.SqlClient
  is a NuGet package. webForCore brings its own build of Microsoft's 4.9.1, `webForCore.Data.SqlClient` (a dependency of
  `webForCore.Web`), whose only change is that `Assembly.GetTypes()` works on it: on every Microsoft package for .NET
  (4.4.3 to 4.9.1) it throws a `ReflectionTypeLoadException` for an unused internal type, `SqlGuidCaster` (step 6). Both
  have the same assembly identity (`System.Data.SqlClient, Version=4.6.1.6`, Microsoft's key) and the same public API,
  so an application, its libraries and webForCore's own code compile and run against either. Exactly one of them is in
  the build, and it is decided the same way in every build (`webForCore.Web.targets`, which the package brings to every
  project that references webForCore, directly or through another project):
  - **Microsoft's**, when the build has Microsoft's `System.Data.SqlClient` package at **4.9.0 or later**: referenced
    by the application itself, or by a project or package it references, such as a data access library shared with a
    solution's workers and services. So an application keeps, or upgrades to, Microsoft's supported package by
    referencing it (`<PackageReference Include="System.Data.SqlClient" Version="4.9.1" />`), and later Microsoft releases
    the same way. webForCore.Data.SqlClient's assemblies are then left out of the compiler's references, the output,
    `deps.json` and a publish.
  - **webForCore's** otherwise. Packages that bring an older Microsoft package transitively (Entity Framework 6.5.1 brings
    4.8.6, ServiceStack.OrmLite.SqlServer 4.8.1) don't change it: theirs is left out.
  - `<WebForCoreSystemDataSqlClient>Microsoft</WebForCoreSystemDataSqlClient>` (or `webForCore`) in the project decides
    it whatever the versions; `Microsoft` without Microsoft's package in the build is error `WFC0195`.

  When both packages are in the build, the build says which one it kept ("webForCore: System.Data.SqlClient is
  Microsoft's, from its System.Data.SqlClient 4.9.1 package; webForCore.Data.SqlClient is left out"). It isn't a
  restore error: NuGet has no way to make two packages of different names conflict, and every Entity Framework
  application has both. A project that doesn't reference webForCore (the shared library itself, its tests, the workers)
  has only Microsoft's package and uses it, as before; it is the same assembly identity and API, so the library's code
  doesn't change between the two.

  Nothing in webForCore needs webForCore's build: `SqlDataSource` (and `ProviderName="System.Data.SqlClient"`),
  `<DbProviderFactories>` and `DbProviderFactories.GetFactory`, the SQL membership, role and profile providers, SQL
  Server session state, `SqlCacheDependency`, `SqlServices` (what `webforcore-regsql` runs; the tool itself carries
  webForCore's build) and LINQ to SQL work on Microsoft's package, as `tools/ci/package-smoke-sql` checks against SQL
  Server with each; typed DataSets' table adapters run on the same `SqlConnection`, `SqlCommand` and `SqlDataAdapter`
  (IIS.Gate.Tests' `DataTests`). webForCore's own scans of an application's types
  (MVC's controllers, Web API's controllers, SignalR's hubs) use the types that load when `GetTypes()` throws, as they
  do on .NET Framework, and find them with Microsoft's package in the application (`tools/ci/package-smoke-mvc`).
  **What an application gives up with Microsoft's package is only #41:** its own code, or a library's, that calls
  `GetTypes()` on every assembly (`AppDomain.CurrentDomain.GetAssemblies()`) without catching
  `ReflectionTypeLoadException` fails on System.Data.SqlClient (Opserver's tabs, some DI containers' and AutoMapper's
  assembly scanning, MEF and plugin loaders). Such an application keeps webForCore's, or catches the exception and uses
  `e.Types.Where(t => t != null)`, as MVC does.

  **Microsoft.Data.SqlClient** (5.x, the successor Microsoft recommends) works beside either, for the application's own
  data access: it is another assembly in another namespace, and the two don't conflict. webForCore's components keep
  `System.Data.SqlClient`, as on .NET Framework, but `SqlDataSource` takes `ProviderName="Microsoft.Data.SqlClient"`, which webForCore registers in `DbProviderFactories` when the application
  references the package. `tools/ci/package-smoke-sql` queries SQL Server with Microsoft.Data.SqlClient 5.2.2 and
  through such a `SqlDataSource`, beside the SQL providers on System.Data.SqlClient.
- **.NET Framework assemblies that are packages on .NET** (#9). The root web.config of .NET Framework referenced its
  framework assemblies for every page, so App_Code and pages used them freely. On .NET some are NuGet packages the
  application must reference, or code compiled at run time fails with `CS1069: The type name 'SyndicationItem' could not
  be found ... forwarded to assembly 'System.ServiceModel.Syndication'` (MiniBlog's feed handler). Among them:
  `System.ServiceModel.Syndication`, `System.Data.SqlClient`, `System.Data.OleDb`, `System.DirectoryServices`,
  `System.Runtime.Caching`, `System.ServiceModel.Http` and the other WCF client packages.
  `System.Data.Services.Client` (WCF Data Services' client: `DataServiceContext`, `DataServiceQuery<T>`, the proxies
  "Add Service Reference" generated for an OData v1-v3 feed) has no .NET version: `CS0234: The type or namespace name
  'Services' does not exist in the namespace 'System.Data'`. Reference **`Microsoft.Data.Services.Client` 5.8.4**, its
  NuGet successor with a .NET Standard build and the same `System.Data.Services.Client` namespace, so generated code
  compiles unchanged (#149, Orchard 1.10's gallery proxy), and remove the `<compilation><assemblies>` entry for
  `System.Data.Services.Client` (section 5). Microsoft.OData.Client 7.x is a different API.
- **A library's own types named like System.Web.ApplicationServices' become ambiguous** (#29). On .NET Framework a
  library that references only `System.Web` doesn't see the membership types (`MembershipUser`, `MembershipCreateStatus`,
  `MembershipProvider`, `Roles`, ...), which `System.Web` forwards to `System.Web.ApplicationServices`. The `webForCore`
  package references `System.Web.ApplicationServices` for every project, so a library that defines its own
  `MembershipUser` and has `using System.Web.Security;` gets `CS0104: 'MembershipUser' is an ambiguous reference`
  (MVCForum). Keep it out of that library's compilation, as it was:
  ```xml
  <PackageReference Include="webForCore.Web.ApplicationServices" Version="0.9.0" ExcludeAssets="compile" />
  ```
- **A package's .NET Standard build can have less than its `net45` build** (#31). NuGet picks the .NET Standard build
  for .NET 10, and some packages built it with a smaller API. WindowsAzure.Storage 9.x has no synchronous blob methods
  there (`Exists`, `OpenRead`, ...: aspnet-mvc5-starter). Hangfire 1.6's has no OWIN extensions (`app.UseHangfireServer`),
  and its SqlServer storage can't look a connection string name up in web.config (MVCForum). Use the version's `net45`
  build, in every project that gets the package, the web project included:
  ```xml
  <PackageReference Include="Hangfire.Core" Version="1.6.17" GeneratePathProperty="true" ExcludeAssets="compile;runtime" />
  <Reference Include="Hangfire.Core" HintPath="$(PkgHangfire_Core)/lib/net45/Hangfire.Core.dll" />
  ```
- **Katana's sign-in and token middleware** (#14, #18). webForCore builds all of Katana 4.2.3's security packages:
  `Microsoft.Owin.Security.OpenIdConnect`, `.Jwt`, `.WsFederation`, `.ActiveDirectory` and the social logins of the
  Visual Studio templates (`.Google`, `.Facebook`, `.MicrosoftAccount`, `.Twitter`), besides `.Cookies` and `.OAuth`.
  Replace each of the application's packages with webForCore's, `webForCore.` and the same name. Before 0.10.0 they
  weren't built and the application kept Microsoft's: the social logins ran (OpenOrderFramework, SocialGoal; every
  template app has `using Microsoft.Owin.Security.Google;` in `Startup.Auth.cs`, even with the providers commented out:
  MvcMovie, Bonobo Git Server), and so did OpenID Connect, but on a mix of IdentityModel 5 and 8 (below). A package
  built on Microsoft's (IdentityServer3.Contrib.AccessTokenValidation on `Microsoft.Owin.Security.Jwt` 4.0.1) gets
  webForCore's as long as the application references webForCore's; the original package is used as it is.
  ```xml
  <PackageReference Include="webForCore.Microsoft.Owin.Security.OpenIdConnect" Version="0.10.0" />
  <PackageReference Include="webForCore.Microsoft.Owin.Security.Jwt" Version="0.10.0" />
  ```
  OpenID Connect, JWT, WS-Federation and Active Directory are built against **IdentityModel 8.19.1**, the version
  CoreWCF (System.Web.Extensions) brings to every webForCore application, where Microsoft's packages use 5.x
  ([NAMESPACES.md](NAMESPACES.md#owin--katana-upstream)). Remove the application's own references to the
  `Microsoft.IdentityModel.*` and `System.IdentityModel.Tokens.Jwt` 5.x packages, which a packages.config conversion
  lists, or reference 8.19.1: a 5.x reference fails restore with `NU1605: Detected package downgrade:
  Microsoft.IdentityModel.Tokens from 8.19.1 to 5.3.0` (Bonobo Git Server, with WS-Federation). The application's own
  code that uses IdentityModel (Microsoft's `OpenIdConnectCachingSecurityTokenProvider` for the Web API samples,
  `JwtSecurityTokenHandler`, `TokenValidationParameters`) then compiles against 8.x. `Startup.Auth.cs` needs no change:
  the OpenID Connect gate runs the "Work or School Accounts" template's, configured as a customer application has it
  for Entra ID (code id_token, form_post, the code redeemed, `SaveTokens`, notifications, its own ticket and state
  formats, JWT and IdentityServer3 bearer tokens on its Web API), and compares it with IIS 10. For Entra ID:
  - **Behind a proxy that ends TLS** (an ingress, a load balancer, App Gateway), set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
    in `webforcore-host`'s environment, so that X-Forwarded-Proto and X-Forwarded-For reach the application. Katana marks
    its nonce cookie `Secure` only on an HTTPS request, and the cookie is `SameSite=None` (the form_post comes from
    Entra ID's site): without `Secure`, browsers drop it, and every sign-in fails with
    `OpenIdConnectProtocolInvalidNonceException` (IDX21323), as on IIS behind such a proxy. The setting also sends the
    browser back to the `https://` URL it asked for: when nothing sets `AuthenticationProperties.RedirectUri`, Katana
    returns to the request's own scheme and host. The `redirect_uri` sent to Entra ID is `RedirectUri` from
    `Startup.Auth.cs` (`ida:RedirectUri`), the same as on IIS; register it in Entra ID as it is.
  - Pages that `<authorization>` in Web.config protects challenge only with `app.UseStageMarker(PipelineStage.Authenticate)`
    after the authentication middleware, as in IIS's integrated pipeline: without it Katana runs at
    `PreHandlerExecute`, which a refused request never reaches. MVC's `[Authorize]` doesn't need it.
  - The back-channel (metadata, keys, the token endpoint) uses `WebRequestHandler` from webForCore's
    `System.Net.Http.WebRequest`, and the OS's certificate store: a Linux container needs `ca-certificates` (Microsoft's
    .NET images have it); Windows containers have the store.
  - The authorize redirect's telemetry says IdentityModel 8 (`x-client-SKU=ID_NET10_0&x-client-ver=8.19.1.0`), and
    IdentityModel's exception messages (IDX...) are 8.x's wording; the exception types are the same.
  - NuGet warns `NU1903` about `Microsoft.Owin` 4.0.1 when a package such as IdentityServer3.Contrib.AccessTokenValidation
    depends on it: webForCore's `Microsoft.Owin` 4.2.3 replaces it in the build, so the warning is about an assembly the
    application doesn't run. `<PackageReference Include="Microsoft.Owin" Version="4.2.3" ExcludeAssets="all" />`
    silences it.
- **Libraries that don't run on .NET** (#22). Remove them or move to a version that does:
  - **Glimpse 1.x** (`Glimpse`, `.AspNet`, `.Ado`, `.EF6`, `.Mvc5`, ...), a development diagnostics tool, can't run:
    Glimpse.Ado's pre-start method reflects into .NET Framework's private `DbProviderFactories` table
    (`NullReferenceException` in `FindDbProviderFactoryTable`), and `Glimpse.AspNet.HttpModule` needs
    `AppDomainSetup.ShadowCopyFiles` (`MissingMethodException`). Remove the packages, the `glimpse` configuration
    section, its module and handler in `<system.web>` and `<system.webServer>`, and the app's `GlimpseSecurityPolicy`
    class (ReaLocate).
  - **AutoMapper before 12** throws while configuring a map (`The type or method has 3 generic parameter(s), but 1
    generic argument(s) were provided`, or `... violates the constraint of type 'T'` from `MaxFloat`/`MaxInteger`): it
    scans `System.Linq.Enumerable`'s extension methods, non-public ones included. This isn't webForCore's: a plain
    .NET 10 console app does the same (AutoMapper 4.2 and 11.0 fail on 10.0.14, as they did on .NET 11). AutoMapper 12
    works; from 4.x the change is mostly `IMapperConfiguration` → `IMapperConfigurationExpression` (ReaLocate). From
    3.x's static `Mapper` it is a rewrite of the mapping code.
  - **Log4Net.Async** 2.0.4 (`ParallelForwardingAppender`, `AsyncForwardingAppender`; net40 only, the last release)
    logs nothing (#95). Each event fails with `TypeLoadException: Could not load type
    'System.Runtime.Remoting.Messaging.CallContext' from assembly 'mscorlib'`: .NET's `mscorlib` has no `CallContext`,
    and webForCore's (in `System.Web`) serves only code compiled against webForCore ([NAMESPACES.md](NAMESPACES.md)).
    log4net swallows the error, so the only sign is an empty log file (the exception shows with
    `<add key="log4net.Internal.Debug" value="true" />` in `<appSettings>`). Point the logger at the appender the
    async one forwards to, so it logs synchronously. Umbraco 7.15.11's `config/log4net.config` has the async appender
    by default:
    ```xml
    <root>
      <priority value="Info"/>
      <appender-ref ref="rollingFile" />   <!-- was: AsynchronousLog4NetAppender (Log4Net.Async.ParallelForwardingAppender) -->
    </root>
    ```
  - **Other prebuilt .NET Framework libraries that use `CallContext`** fail the same way, with `TypeLoadException: Could
    not load type 'System.Runtime.Remoting.Messaging.CallContext' from assembly 'mscorlib, Version=4.0.0.0'` (#178).
    Look for a version with a .NET Standard or .NET build, which NuGet then picks. BlogEngine.NET's SimpleInjector
    3.1.0: `.Integration.WebApi`'s `BeginScope` uses `SimpleInjector.Extensions.ExecutionContextScoping` 3.1.0 (both
    its builds use `CallContext`), so every Web API controller failed with "Make sure that the controller has a
    parameterless public constructor", the `TypeLoadException` inside. SimpleInjector, `.Extensions.ExecutionContextScoping`
    and `.Integration.WebApi` 3.3.2 work: that ExecutionContextScoping has a .NET Standard 1.3 build (on `AsyncLocal`).
  - **Autofac.Configuration 3.x** (3.3.0, the last for Autofac 3) throws reading a configuration file (#148, Orchard
    1.10): `MissingMethodException: Method not found: 'System.String System.AppDomainSetup.get_ConfigurationFile()'`
    in `SectionHandler.NormalizeConfigurationFilePath`. 4.x is a rewrite on Microsoft.Extensions.Configuration and
    needs Autofac 4. To keep Autofac 3, read the file yourself: `SectionHandler.Deserialize(XmlReader)` takes the
    same XML, from a `ConfigurationModule` subclass that sets `SectionHandler`.
  - **Castle.Core 3.x** (DynamicProxy; Autofac's interception) fails creating every proxy: `MissingMethodException:
    Method not found: 'System.Reflection.Emit.AssemblyBuilder System.AppDomain.DefineDynamicAssembly(...)'` in
    `Castle.DynamicProxy.ModuleScope.CreateModule`. Use 4.0 to 4.1.1, which have 3.x's API apart from
    `Castle.Core.Internal`'s `ForEach` extension, now internal. 4.2 added `Trace` members to
    `Castle.Core.Logging.ILogger`, which an application's own logger then has to implement.
  - **NHibernate 4.x** (4.1.2.4000, the last) fails creating lazy-loading proxies, with the same
    `DefineDynamicAssembly` exception (`HibernateException: Creating a proxy instance failed`, then
    `LazyInitializationException: illegal access to loading collection`), and with `adonet.batch_size` above 0 on SQL
    Server, `TypeInitializationException` in `NHibernate.AdoNet.SqlClientSqlCommandSet`. Use 5.1 or later, with
    FluentNHibernate 2.1.2 and Iesi.Collections 4.0.4: it works on webForCore's System.Data.SqlClient, batching
    included. What changed for Orchard: `IInterceptor.Instantiate(string entityName, object id)` (no `EntityMode`),
    `DriverBase.InitializeParameter(DbParameter, ...)`, `new EntityKey(id, persister)`,
    `IClassMetadata.GetPropertyValues(obj)`/`SetPropertyValues(obj, values)`, `ICache`'s new async members, and
    NHibernate.Caches.SysCache2 5.x.

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
        builder.Services.AddWebForms();                 // IIS compression, W3C logging

        var app = builder.Build();
        app.UseWebForms(o => o
            .HandleAllRequestsWithWebForms());          // routing, extensionless URLs, static files through IIS rules
        app.Run();
    }
}
```

Without `HandleAllRequestsWithWebForms()`, only ASP.NET's own extensions (`.aspx`, `.ashx`, `.asmx`, `.axd`, ...)
reach System.Web, and ASP.NET Core serves everything else.

Session state needs nothing here when it was in process on IIS (`<sessionState mode="InProc">`, or no `mode`: the
default). Only an application whose session state was out of process adds a distributed cache and
`UseAspNetCoreSessionProvider()` ("Session state", next).

### Session state

**In process (`mode="InProc"`, the default), as on IIS.** With no `UseAspNetCoreSessionProvider()`, System.Web's
`SessionStateModule` keeps sessions in its own `InProcSessionStateStore` (.NET Framework 4.8's, unchanged), in the
application's cache: a value is the object the application stored, not a copy, so it needn't be serializable
(MVC's `TempData` included), and sessions are lost when the application restarts (a recycle,
a `web.config` change), as on IIS. Keep it this way for an application that ran in process on IIS, which is most of
them: changing it would make every session value go through `BinaryFormatter` (#180).

**Out of process: `UseAspNetCoreSessionProvider()`.** It keeps session state in ASP.NET Core's distributed cache
(`IDistributedCache`), as `<sessionState mode="StateServer">` or `"SQLServer"` kept it out of process on IIS. Use it
where the application's session state was out of process on IIS (`mode="StateServer"`, `"SQLServer"` or `"Custom"`
with a provider that stored it elsewhere, such as Redis's), or to share sessions between servers (scaling out):

```csharp
builder.Services.AddDistributedMemoryCache();   // one server; or AddStackExchangeRedisCache(...),
                                                // AddDistributedSqlServerCache(...) to share it between servers
...
app.UseWebForms(o => o.HandleAllRequestsWithWebForms().UseAspNetCoreSessionProvider());
```

It takes the place of `<sessionState mode>` (whatever it says) and of a custom provider; the rest of `<sessionState>`
applies, as on IIS:

- **Every session value must be serializable**, as with StateServer and SQLServer: each one is serialized at the end
  of the request. A value that isn't (a view model in MVC's `TempData` that isn't `[Serializable]`, MVCForum's
  `GenericMessageViewModel`) fails the request with `HttpException: Unable to serialize the session state. In
  'StateServer' and 'SQLServer' mode, ASP.NET will serialize the session state objects, ...` and
  `SerializationException: Type '...' is not marked as serializable` at
  `AspNetCoreSessionProvider.SetAndReleaseItemExclusive`. An application that ran in process on IIS doesn't need the
  provider: leave it out. Values are copies: changing an object read from the session changes the session only when
  the request ends, as with StateServer.
- The session ID and its cookie are System.Web's (`SessionIDManager`): `ASP.NET_SessionId`, or `<sessionState cookieName>`,
  with `SameSite=Lax` (`<sessionState cookieSameSite>`). It is the only session cookie: ASP.NET Core's
  session (`AddSession()`, `UseSession()` and its `.AspNetCore.Session` cookie) isn't used, and isn't needed (#79).
  Leave them in only if ASP.NET Core code of the application uses `HttpContext.Session` itself.
- A session ends `Session.Timeout` minutes (`<sessionState timeout>`, 20 by default) after its last request; ASP.NET
  Core's `SessionOptions.IdleTimeout` doesn't apply. `Session_End` isn't raised, as with IIS's out-of-process modes.
- Several applications sharing one cache need an instance name each (Redis's `InstanceName`, a table each for SQL
  Server): the cache keys are `WebFormsForCore.SessionState.` and the session ID.

Up to 0.7.0, the provider kept session state inside ASP.NET Core's session, which set a second cookie
(`.AspNetCore.Session`), needed `UseSession()`, and ended sessions after `SessionOptions.IdleTimeout`. Sessions stored
before the upgrade aren't found after it: users start new ones.

## 5. Configuration

- **Keep `web.config`**, including `<system.webServer>`: webForCore reads its modules, handlers, rewrite rules,
  request filtering, default documents, custom errors and static content settings. See
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md) for what's covered.
- **Remove `<compilation><assemblies>` entries for assemblies .NET doesn't have**, such as System.Design,
  System.Data.Services.Client or System.IdentityModel
  ([NAMESPACES.md](NAMESPACES.md)). Each one is a Configuration Error on every page ("Could not load
  file or assembly 'System.Design, Version=4.0.0.0'"), even when no page uses it (#37, Subtext and mojoPortal).
  System.Data.Linq's can stay from 0.9.0, at 3.5.0.0 or 4.0.0.0: the `webForCore` package brings it (#142).
- **Set an explicit `<machineKey>`** (validation and decryption keys). Generated keys change with every process, so
  forms authentication cookies, view state and anti-forgery tokens wouldn't survive a restart or work across servers.
- **`<trust level="Medium" />`** (or any partial trust level) can stay: it is ignored and the application runs in full
  trust (#87). Code that worked around Medium trust keeps working; code that checks the trust level sees full trust.
- **Connection strings:** `System.Data.SqlClient` is used, as on .NET Framework. Add `TrustServerCertificate=True` or
  a trusted certificate if the server's certificate isn't trusted on Linux. LocalDB doesn't exist on Linux.
- **The SQL providers' database (`aspnetdb`)** (#123). The default `SqlMembershipProvider`, `SqlRoleProvider`,
  `SqlProfileProvider`, personalization and `SqlWebEventProvider` use machine.config's `LocalSqlServer`: SQL Express with
  `AttachDBFilename=|DataDirectory|aspnetdb.mdf;User Instance=true`, which ASP.NET created on first use. That can't
  exist on Linux: the first sign-in or registration is a 500 ("SQLExpress database file auto-creation error", MVC Music
  Store). Point `LocalSqlServer` at a SQL Server in the application's web.config:

  ```xml
  <connectionStrings>
    <remove name="LocalSqlServer" />
    <add name="LocalSqlServer" providerName="System.Data.SqlClient"
         connectionString="Data Source=db.example.com;Initial Catalog=aspnetdb;User ID=app;Password=...;TrustServerCertificate=True" />
  </connectionStrings>
  ```

  Then create the database once, as `aspnet_regsql` did, with `webforcore-regsql`, which has its switches (from 0.9.0;
  `dotnet tool install -g webForCore.RegSql --add-source https://xforcore.github.io/webForCore/index.json`):

  ```bash
  webforcore-regsql -S db.example.com -U sa -P '...' -d aspnetdb -A mr
  ```

  `-A all`, or the letters `m` (membership), `r` (roles), `p` (profile), `c` (personalization) and `w` (web events);
  `-C <connection string>` instead of `-S`/`-U`/`-P`; `-sqlexportonly <file>` writes the script instead, for a DBA to
  run. SQL Server session state is `-ssadd -sstype t|p|c`. An application can also call
  `System.Web.Management.SqlServices.Install(...)`, as on .NET Framework. The scripts are .NET Framework 4.8's, which
  System.Web carries, so an `aspnetdb` that served the application on IIS works as it is: move the database with its
  users.
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
  `Path.Combine` with `\`) and file names whose case differs from the references to them in code that opens files
  itself (`File.ReadAllText`, `new FileStream`). `Server.MapPath`, virtual paths and URLs work as before: a URL finds
  its page, handler, static file or folder in any case, as on IIS (#140).
- **Telling URLs and paths apart** (#92). Two .NET behaviours on Linux (Unix, not webForCore) break code that checks
  whether a string is a local URL or a physical path:
  - `Uri.TryCreate("/umbraco/Default.aspx", UriKind.Absolute, out uri)` succeeds: a path that starts with `/` is an
    implicit `file:` URI (`IsFile` true, `Host` `""`). On Windows and .NET Framework it isn't absolute. Umbraco 6.2.6's
    log-in checks its `redir` this way, finds the host `""` isn't the request's, and throws "There was attempt to
    redirect to '/umbraco/Default.aspx' which is another domain", so every visit to `/umbraco/` is a 500. Search for
    `UriKind.Absolute` (open-redirect checks, link rewriters, proxy allow lists) and treat a string that starts with
    `/` as local before calling it, or check `uri.IsFile`. `UriKind.RelativeOrAbsolute` gives a relative URI for
    `/path` on Linux too.
  - `Path.VolumeSeparatorChar` is `/` (`:` on Windows). Umbraco 6.2.6's `IOHelper.MapPath` took
    `path[1] == Path.VolumeSeparatorChar` to mean "already a physical path", so `~/...` came back unmapped: its
    language files weren't found and files were written under a folder named `~` in the site. Search for
    `VolumeSeparatorChar`, and use `Path.IsPathRooted` or `Path.IsPathFullyQualified` instead.
- **The build output is `bin_dotnet/`** (#91). Code or configuration that builds `~/bin/...` paths to load or scan the
  application's assemblies (plug-in folders, `Assembly.LoadFrom`, type scans) finds nothing there:
  `FileNotFoundException: Could not load file or assembly '<site>/bin/umbraco.dll'` when Umbraco 6.2.6 creates a
  document type (it loads its dialogs' tasks from `~/bin` + the assembly in `config/create/UI.xml`). Use
  `HttpRuntime.BinDirectory`, which is `<site>/bin_dotnet/` on webForCore, or point the setting at `~/bin_dotnet`
  (Umbraco has one: `<add key="umbracoBinDirectory" value="~/bin_dotnet" />`). Search for `~/bin` and `bin\`.
- **The site's folder:** `AppDomain.CurrentDomain.BaseDirectory` (and `AppContext.BaseDirectory`) is the build output,
  `bin_dotnet/`, not the site root as in IIS's per-application AppDomain. Find the site's files with
  `HttpRuntime.AppDomainAppPath`, `HostingEnvironment.ApplicationPhysicalPath` or `Server.MapPath("~/...")` instead
  ([NAMESPACES.md](NAMESPACES.md)). Search for `BaseDirectory` and `ApplicationBase`: eShopLegacyMVC's database seed read
  its SQL scripts that way.
  Libraries resolve their own relative paths against it too (#173): log4net's `XmlConfigurator` `ConfigFile` and an
  appender's `<file>`, NLog's `${basedir}`. `bin_dotnet` is the application's `bin`, so a file the application writes
  there restarts it, as a file written into `bin` does on IIS. mojoPortal's log4net appender
  (`<file value="Data/currentlog.config" />`) wrote into `bin_dotnet/Data`, and since it logs at start, the
  application restarted in a loop, losing session state each time. Point such paths at the site
  (`<file value="../Data/currentlog.config" />` from `bin_dotnet/`, or a path set from `HttpRuntime.AppDomainAppPath`
  before configuring the library). Search the logging configuration for relative file names.
- **`AppDomain.CurrentDomain.DynamicDirectory` is null** on .NET (#139). On IIS it was the application's code
  generation folder (`Temporary ASP.NET Files/<app>/<hash>`), where plug-in loaders (nopCommerce, SmartStoreNET, and
  others descended from Umbraco's) copy plug-in DLLs before `Assembly.Load`ing them and adding them to `BuildManager`.
  They fail at start: `ArgumentNullException: Value cannot be null. (Parameter 'path') at
  System.IO.DirectoryInfo..ctor(String path)`. Use `HttpRuntime.CodegenDir`, the same folder, which webForCore's
  assembly resolver probes: with it nopCommerce 3.90's plug-ins load as on IIS. Likewise `AppDomainSetup.PrivateBinPath`
  doesn't exist on .NET (`CS1061`): use `HttpRuntime.BinDirectory`. `AppDomain.CurrentDomain.RelativeSearchPath`,
  the application's `bin` on IIS, compiles but is null (#177): CarrotCake CMS's resource handler built
  `Path.Combine(AppDomain.CurrentDomain.RelativeSearchPath, fileName)` and threw `ArgumentNullException: Value cannot
  be null. (Parameter 'path1')`. Use `HttpRuntime.BinDirectory` there too. Search for `DynamicDirectory`,
  `PrivateBinPath` and `RelativeSearchPath`.
- **`SecurityProtocolType.Ssl3` throws** (#176, CarrotCake CMS): .NET's `ServicePointManager.SecurityProtocol` setter
  rejects SSL 3, `NotSupportedException: The requested security protocol is not supported.`, where .NET Framework 4.8
  accepted it among the flags. The "enable TLS 1.2" line of 4.5-era applications often kept it
  (`SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | ... | SecurityProtocolType.Tls12`). Remove `Ssl3`, or the
  whole line: .NET's default, `SystemDefault`, lets the OS choose. Search for `SecurityProtocolType.Ssl3`.
- **`Thread.ResetAbort()` throws** `PlatformNotSupportedException: Thread abort is not supported on this platform.`
  (#143, DNN 9's URL rewriter and module host). Where it follows `catch (ThreadAbortException)` (after `Response.End`,
  `Response.Redirect(url, true)` or `Server.Transfer`), remove it: webForCore's `ThreadAbortException` isn't
  re-raised at the end of the `catch`, so the `catch` already ends it. Search for `ResetAbort`.
- **The parameterless crypto factories throw** (#151, Kooboo CMS's password hashing): on .NET Framework they returned
  machine.config's defaults; on .NET they throw `PlatformNotSupportedException: This platform does not allow the
  automatic selection of an algorithm.` Replace each with the old default, not a stronger algorithm, or stored hashes
  and encrypted values stop matching. The named overloads (`HashAlgorithm.Create("SHA256")`) still work.

  | Call | .NET Framework 4.8 returned | Use |
  | --- | --- | --- |
  | `HashAlgorithm.Create()` | `SHA1CryptoServiceProvider` | `SHA1.Create()` |
  | `KeyedHashAlgorithm.Create()`, `HMAC.Create()` | `HMACSHA1` | `new HMACSHA1()` |
  | `SymmetricAlgorithm.Create()` | `RijndaelManaged` | `Aes.Create()` (the same cipher at `RijndaelManaged`'s defaults: 128-bit blocks, 256-bit key, CBC, PKCS7) |
  | `AsymmetricAlgorithm.Create()` | `RSACryptoServiceProvider` | `RSA.Create(1024)` (its default key size) |
- **Compiling C# at run time** (#133). .NET's System.CodeDom can't compile: `CodeDomProvider.CreateProvider("c#")`
  returns .NET's `Microsoft.CSharp.CSharpCodeProvider`, whose `CompileAssemblyFromSource`, `...FromDom` and
  `...FromFile` throw `PlatformNotSupportedException: Operation is not supported on this platform.`, and it reads no
  `<system.codedom>` (C1 CMS's data types). Use webForCore's providers, which System.Web compiles pages with
  (in-process Roslyn, C# and VB). `WebFormsForCore.CodeDom.Compiler.CodeDomProvider.CreateProvider("c#")` reads
  `<system.codedom>` as .NET Framework's did, and `new WebFormsForCore.CodeDom.Compiler.CSharpCodeProvider()` (or
  `VBCodeProvider`) creates one directly. They take System.CodeDom's `CompilerParameters` and return its
  `CompilerResults`, so only the provider's type changes (in a file that names it,
  `using CodeDomProvider = WebFormsForCore.CodeDom.Compiler.CodeDomProvider;`); they don't derive from
  System.CodeDom's `CodeDomProvider`, so code that passes one to an API typed with it needs that API changed too.
  `CompilerParameters`' options (`GenerateInMemory`, `IncludeDebugInformation`, `CompilerOptions`, `OutputAssembly`, `TempFiles.KeepFiles`)
  give the results and leave the files .NET Framework 4.8's provider did; up to 0.8.0, `CompilerOptions = null` threw a
  `NullReferenceException` and `GenerateInMemory` without debug information an `ArgumentNullException` (#135). Two
  differences remain. A reference by file name alone (`ReferencedAssemblies.Add("System.dll")`) isn't found
  (`CS0006: Metadata file 'System.dll' could not be found`): .NET Framework's csc looked in the framework folder;
  give the path, `Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.dll")`. And, as Microsoft's DotNetCompilerPlatform provider they are built
  from does, they put `/nowarn:1659;1699;1701;612;618` in front of the parameters' `CompilerOptions`.
- **Newlines:** `Environment.NewLine` is `\n`.
- **Fonts and drawing:** install the fonts the app uses, or fontconfig's metric-compatible substitutes for Windows
  fonts (Liberation, Carlito, Caladea).
- **Culture data:** ICU's formats differ slightly from Windows NLS (for example some currency and date patterns).
  Check pages that format numbers and dates. One changes ordinary pages (#124): ICU gives `en-US` and `en-GB` a
  `NumberFormatInfo.NumberDecimalDigits` of 3 (Windows has 2; the invariant culture still has 2), so `F` and `N` with
  no precision print three decimals: `String.Format("{0:F}", 8.99m)` is `8.990`, not `8.99` (MVC Music Store's
  prices). Give a precision where the page relies on two (`{0:F2}`, `ToString("N2")`), or set `NumberDecimalDigits`
  on the culture the application uses at start.
- **Files without a BOM** (#90): `.aspx`, `.ascx`, `.master`, `.cshtml` and the other files ASP.NET compiles are read
  in the ANSI code page when they have no byte order mark and `<globalization fileEncoding>` isn't set, as on .NET
  Framework, whose `Encoding.Default` it was (.NET's is UTF-8). On Windows that is the system's code page; Linux has
  none, so it is the one Windows has for the locale (`LANG`): windows-1252 for English, Western European and the
  invariant culture (`LANG=C`), 932 for Japanese, 1251 for Russian, and so on. Files saved in that code page on the old
  server (Umbraco 6's installer has `’` as byte `0x92`) show as they did there. If the server you're moving from had
  another code page, set it: `<globalization fileEncoding="windows-1251" />`. Files with a BOM are read in its
  encoding; files that are UTF-8 without one (saved by an editor other than Visual Studio) need
  `<globalization fileEncoding="utf-8" />`, as they did on IIS, or `’` shows as `â€™`.
- **A server with no locale runs with the invariant culture** (#97). With `LANG` and `LC_ALL` unset, or `C`, `POSIX`
  or `C.UTF-8` (GitHub's Ubuntu runners, most container images), .NET's `CurrentCulture` and `CurrentUICulture` are
  `CultureInfo.InvariantCulture` (name `""`), which can't happen on IIS. Resources and language files chosen by culture
  name aren't found (Umbraco 6.2.6's log-in page shows `[login]`, `[username]`, `[password]` and can't log in or
  install), a `ResourceManager` falls back to the neutral resources, and dates and numbers are formatted
  `11/03/2026` and `¤1,234.50`. Set the culture in `web.config`, which applies to every request as on IIS:
  ```xml
  <globalization culture="en-US" uiCulture="en-US" />
  ```
  or give the server's environment a locale (`LANG=en_US.UTF-8`). Code outside requests (background threads,
  timers) gets only the process's culture, so set `LANG` too where that matters. The application logs a warning when
  it starts with the invariant culture and `<globalization>` sets no `culture` or `uiCulture` (`auto` alone counts as
  none: it falls back to the process's culture): `Application ... runs with the invariant culture`.
- **Named semaphores and events are Windows-only on .NET** (#94). `new Semaphore(..., name)`,
  `new EventWaitHandle(..., name)` and their `OpenExisting` throw `PlatformNotSupportedException: The named version
  of this synchronization primitive is not supported on this platform.` on Linux; only a named `Mutex` works, and
  across processes. Umbraco 7.15.11's `MainDom` creates both in `ApplicationContext.Init`, so it fails in
  `Application_Start`. Such code coordinated an application's old and new AppDomain during a recycle; under
  `webforcore-host` a recycle is a new worker process (section 8), so the replacement still has to work across
  processes: a named `Mutex` (it must be released by the thread that acquired it, so it isn't a drop-in for a
  semaphore released elsewhere) or a file lock. Where the old instance only needs to let go when it stops, process-local
  primitives (unnamed) are enough: the old worker still gets `IRegisteredObject.Stop` when the host stops it.
- **BinaryFormatter:** code that calls `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter` directly
  should use `WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter`.
- **LINQ to SQL** (#142) works as it is against SQL Server: `DataContext` classes, attribute mapping (what the `.dbml`
  designer generates) and `XmlMappingSource` need no changes, and errors have .NET Framework's exception types and
  messages. The `webForCore` package brings System.Data.Linq; remove a `<Reference>` to a copy of .NET Framework's.
  Three things need SQL Server and a connection string instead: SQL Server Compact (`.sdf`), and off Windows a `.mdf` path or
  LocalDB (see [NAMESPACES.md](NAMESPACES.md#summary-of-what-doesnt-work)). The SQL it generates has the platform's
  newline, `\n` on Linux, as .NET Framework's `SqlFormatter` writes it with `AppendLine`: in `DataContext.Log` and in
  `GetCommand(query).CommandText` (#175). CarrotCake CMS's batch delete and update helper, a pattern several LINQ to SQL
  applications copied, looked for `"\r\nFROM "` in the generated SELECT and threw `ArgumentOutOfRangeException:
  startIndex ('-1')` on Linux. Search for `"\r\n` in code that reads SQL text, and use `Environment.NewLine`.
  `LinqDataSource` isn't available yet: bind an `ObjectDataSource` or model
  binding to the `DataContext`'s queries. Web API OData's `ODataConventionModelBuilder` needs System.Data.Linq for
  its `Binary` mapping, so OData applications, which failed in `Application_Start` before 0.9.0, now start.
- **Scanning every loaded assembly's types** (#41, fixed). `Assembly.GetTypes()` on Microsoft's System.Data.SqlClient
  packages throws on .NET, on Linux and on Windows: `ReflectionTypeLoadException ... Could not load type
  'SqlGuidCaster' ... contains an object field at offset 0 that is incorrectly aligned or overlapped by a non-object
  field`. Every System.Data.SqlClient package for .NET (4.4.3 to 4.9.1) has this unused type, so code that calls
  `GetTypes()` on every assembly in `AppDomain.CurrentDomain.GetAssemblies()` failed (Opserver finds its tabs that way;
  DI containers' assembly scanning, AutoMapper profiles, MEF and plugin loaders often do too). webForCore builds
  System.Data.SqlClient from Microsoft's source with the type fixed (`webForCore.Data.SqlClient`, which `webForCore.Web`
  brings), and leaves the `System.Data.SqlClient` package that Entity Framework and others bring out of the build, so
  such scans work without changes. An application still on an earlier webForCore can catch the exception and use the
  types that loaded (`e.Types.Where(t => t != null)`). An application that references Microsoft's System.Data.SqlClient
  4.9.0 or later gets Microsoft's, on which `GetTypes()` throws again (step 3, #195).

## 7. Run and compare

```bash
dotnet build -c Release
dotnet bin_dotnet/YourApp.dll --urls http://localhost:5000     # from the site folder
```

The first request compiles pages, as on IIS. Then compare the responses recorded in step 3 of "Before you start"
(status codes, headers, bodies), and run the app's own tests. Also check that `/bin_dotnet/`, `/App_Data/`, `.cs` and
`.config` files return 404.

## 8. Deploy

- **Under the host.** The `webForCore` package puts `webforcore-host` in the build output
  (`bin_dotnet/webforcore-host/`). It plays the application pool's part: it owns the bindings and runs the app in
  worker processes. Deploy the site folder (`web.config` at the top, the build in `bin_dotnet/`) and run
  `dotnet bin_dotnet/webforcore-host/webforcore-host.dll` from it. Starting the host at boot, restarting it if it
  ends, and its identity and resource limits are your service manager's job (systemd, a container runtime, a Windows
  service), as they were Windows' for IIS: webForCore doesn't ship service definitions.
- **Bindings belong to the host,** as they were IIS's: `--urls`, `ASPNETCORE_URLS`, or the `Kestrel` section of
  `appsettings.json` (endpoints, certificates, `ClientCertificateMode`). The app's workers serve only the host, so
  endpoints the app's `Program.cs` sets up itself (`ConfigureKestrel(k => k.Listen(...))`) would conflict: move them to
  configuration. The host's own settings (startup and shutdown time limits, rapid-fail protection) are in the
  `WebForCore:Host` section ([IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#process-model)).
- **In front:** a reverse proxy (nginx, Apache, a load balancer) for TLS and the public port, or the host's own HTTPS.
- **Recycling:** a `web.config`, `bin` or `App_Code` change restarts the app in a new worker process, as IIS did; no
  request fails (the old worker finishes against the assemblies it compiled, #89), and those that arrive meanwhile
  wait for the new worker. Set time, request or memory recycling with `UseWebForms(o => o.Recycling(...))`: such a
  recycle is overlapped, as IIS's was, and the old worker serves until the new one is ready. Run without
  the host (`dotnet bin_dotnet/YourApp.dll`), the app stops when it restarts, for whatever started it to start it
  again.
- **Application pool settings** (#193) go in the `WebForCore:Host` section, from the pool in applicationHost.config:
  `startMode` is `StartMode` (the host's default is `AlwaysRunning`, IIS's `OnDemand`), `<processModel idleTimeout>`
  is `IdleTimeout` (the host's default is off; IIS's was 20 minutes: set `"IdleTimeout": "00:20:00"` only if the pool
  relied on it), and the application's `preloadEnabled` is `PreloadEnabled`. `<applicationInitialization>` stays in
  `web.config` and works as on IIS: its `initializationPage` requests warm each new worker before it takes traffic, so
  that after a start or a recycle a load balancer's health check doesn't reach a cold application, and with
  `doAppInitAfterRestart="true"` after a `web.config`, `bin` or `App_Code` change too. A URL Rewrite rule that tests
  `{APP_WARMING_UP}` never matches (the variable isn't set); use `remapManagedRequestsTo` for a splash page. See
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#process-model).
- **Precompilation** (optional): `aspnetcore_compiler -v / -p <site> -b bin_dotnet [-u] <target>`, then copy the build
  into `<target>/bin_dotnet`. Use `-u` for updatable sites.
- **Scaling out:** use out-of-process session state (Redis or SQL Server through ASP.NET Core's distributed cache) and
  the same `<machineKey>` everywhere.
  SignalR needs a backplane too ([below](#scaling-out-signalr)).
- Several processes must not share one site folder: they share the compilation folder.

### Scaling out SignalR

On one instance SignalR keeps its messages in that process: behind a load balancer, a client connected to one instance
gets nothing a hub sends on another. As on IIS, a **backplane** joins the instances' message buses. webForCore builds
Microsoft's three that run on .NET, from SignalR 2.4.3's source, with Microsoft's names and versions, so the code and
configuration an IIS web farm has work unchanged:

| Backplane | Package (in place of Microsoft's) | In `Startup.Configuration`, before `MapSignalR` |
| --- | --- | --- |
| Redis, on StackExchange.Redis 2 | `webForCore.Microsoft.AspNet.SignalR.StackExchangeRedis` (`Microsoft.AspNet.SignalR.StackExchangeRedis`) | `GlobalHost.DependencyResolver.UseStackExchangeRedis(new RedisScaleoutConfiguration("redis:6379,password=...", "MyApp"));` |
| Redis, on StackExchange.Redis.StrongName 1.x | `webForCore.Microsoft.AspNet.SignalR.Redis` (`Microsoft.AspNet.SignalR.Redis`) | `GlobalHost.DependencyResolver.UseRedis("redis", 6379, "password", "MyApp");` |
| SQL Server | `webForCore.Microsoft.AspNet.SignalR.SqlServer` (`Microsoft.AspNet.SignalR.SqlServer`) | `GlobalHost.DependencyResolver.UseSqlServer(ConfigurationManager.ConnectionStrings["SignalR"].ConnectionString);` |

- **Connection settings** are the backplane's own, as on .NET Framework: a StackExchange.Redis configuration string
  (`host:port,password=...,ssl=true,abortConnect=false`) and an event key, the Redis channel every instance of the
  application shares; or a SQL Server connection string (with `TrustServerCertificate=True` or a trusted certificate,
  as above). The SQL Server backplane creates its `[SignalR]` schema and tables at start, so its login needs
  `CREATE SCHEMA` and `CREATE TABLE` the first time; turn Service Broker on in its database (`ALTER DATABASE ... SET
  ENABLE_BROKER`) and it is notified of each message, otherwise it polls. `SqlScaleoutConfiguration.TableCount` spreads
  the messages over several tables. Azure SQL Database isn't supported, as upstream.
- **Sticky sessions aren't needed** with a backplane, as on IIS: any instance can serve any request of a connection,
  long polling's polls and sends included. Every instance needs **the same `<machineKey>`**: SignalR protects its
  connection and groups tokens with it, so a token one instance gave must read on the others and on an instance after
  it restarts. With the keys the same, a client whose instance restarts reconnects to it (or to another) with its
  connection and groups.
- The **Redis and StackExchange.Redis packages' dependencies** (StackExchange.Redis.StrongName, StackExchange.Redis) are
  NuGet packages, restored as they are: webForCore ships only Microsoft's assemblies.
- **Azure Service Bus** (`Microsoft.AspNet.SignalR.ServiceBus`, `.ServiceBus3`) isn't built: `WindowsAzure.ServiceBus`
  is .NET Framework only. Move to the Redis or SQL Server backplane (or Azure Cache for Redis).
- The IIS gate tests run two instances of an application, each under its own `webforcore-host`, on each backplane
  (`ScaleoutTests`, [TESTING.md](TESTING.md)).

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `Could not load file or assembly 'System.Web'... conflicts with what is already bound` | `webForCore.WebStack.targets` isn't imported |
| `.cshtml` build errors from the ASP.NET Core Razor SDK | Razor SDK compilation isn't turned off (step 2) |
| `Type 'System.Web.Script.Services...' not found`, ScriptModule errors | System.Web.Extensions or System.Web.Services isn't referenced |
| 404 for files that exist | Case mismatch in the path, or a request filtering rule (`<hiddenSegments>`, `<fileExtensions>`) |
| Logins lost after a restart | No explicit `<machineKey>` |
| `PlatformNotSupportedException` | A Windows-only API: see [NAMESPACES.md](NAMESPACES.md) |
| `PlatformNotSupportedException: The named version of this synchronization primitive is not supported` | A named `Semaphore` or `EventWaitHandle`: see "Named semaphores and events" (step 6) |
| `CS0121: The call is ambiguous` | The app's own `DistinctBy`/`MaxBy`/`Chunk`, or a `params` call with no arguments (step 2) |
| `CS0104: 'KeyValuePair' is an ambiguous reference` (or another type .NET added) | Alias the app's own type in that file (step 2) |
| `CS0104: 'Compare' is an ambiguous reference`; `Conflicting versions of ASP.NET Web Pages detected` | An MVC 3 or 4 app ("Before you start", step 2) |
| `BC30002: Type 'Global.Microsoft.VisualBasic.ApplicationServices.ApplicationBase' is not defined` | `<MyType>Windows</MyType>` in a VB project: use `Empty` (step 2) |
| `CS0103` in `CodeFile=` code-behind, in a project with no old `.csproj` | A Web Site Project: compile only `Program.cs` (step 2) |
| `CS0234: ... 'Design' does not exist in the namespace 'System.Web.UI'` | The app's designer classes: remove them from the build (step 2) |
| ``LINQ to Entities does not recognize the method 'Boolean Contains[Int32](System.ReadOnlySpan`1[System.Int32], Int32)'`` | C# 14's span conversions: set `<LangVersion>` (step 2) |
| `HttpException: Unable to serialize the session state`, `SerializationException: Type '...' is not marked as serializable` at `AspNetCoreSessionProvider.SetAndReleaseItemExclusive` (often after a redirect that keeps a view model in `TempData`) | `UseAspNetCoreSessionProvider()` in an application whose session state was in process on IIS: remove it and its `AddDistributedMemoryCache()` (section 4, "Session state"). If the application needs it (out of process on IIS, or scaling out), make the type `[Serializable]` |
| `NU1107: Version conflict detected for Microsoft.CodeAnalysis.Common` | The app's own Roslyn packages: use 5.9.0 (step 3) |
| `CS0234: ... 'Services' does not exist in the namespace 'System.Data'` | WCF Data Services' client: `Microsoft.Data.Services.Client` 5.8.4 (step 3) |
| `FileNotFoundException` for a plug-in's package when the plug-in loads | SDK libraries don't copy packages: "Plug-in libraries" (step 2) |
| `MissingMethodException: Method not found: '... AppDomainSetup.get_ConfigurationFile()'` or `'... AppDomain.DefineDynamicAssembly(...)'` | Autofac.Configuration 3, Castle.Core 3 or NHibernate 4 (step 3, "Libraries that don't run on .NET") |
| `ArgumentNullException ... (Parameter 'path')` at `DirectoryInfo..ctor` in a plug-in loader | `AppDomain.CurrentDomain.DynamicDirectory` is null: use `HttpRuntime.CodegenDir` (step 6) |
| `ArgumentNullException ... (Parameter 'path1')` at `Path.Combine` | `AppDomain.CurrentDomain.RelativeSearchPath` is null: use `HttpRuntime.BinDirectory` (step 6) |
| The application restarts again and again (its start logged repeatedly, sessions lost) | A library writes a file into `bin_dotnet`, such as log4net's log at a relative path (step 6, "The site's folder") |
| `NotSupportedException: The requested security protocol is not supported` | `SecurityProtocolType.Ssl3` in `ServicePointManager.SecurityProtocol` (step 6) |
| `Parser Error Message: Unexpected "string" keyword after "@" character` (or another C# keyword) | A Razor 1 view: wrap it in `@( ... )` ("Before you start", step 2) |
| `ArgumentOutOfRangeException: startIndex ('-1')` in code that searches SQL or other generated text | It looks for `\r\n`; the text has `\n` on Linux (step 6, "LINQ to SQL") |
| `TypeLoadException: Could not load type 'System.Runtime.Remoting.Messaging.CallContext' from assembly 'mscorlib'`, or "Make sure that the controller has a parameterless public constructor" with it inside | A prebuilt .NET Framework library uses `CallContext`: a version with a .NET Standard or .NET build (step 3, "Libraries that don't run on .NET") |
| `PlatformNotSupportedException: Thread abort is not supported on this platform` | `Thread.ResetAbort()`: remove it (step 6) |
| `PlatformNotSupportedException: This platform does not allow the automatic selection of an algorithm` | `HashAlgorithm.Create()` or another parameterless crypto factory (step 6) |
| `PlatformNotSupportedException` from `CompileAssemblyFromSource`, `...FromDom` or `...FromFile` | System.CodeDom can't compile on .NET: use webForCore's provider (step 6) |
| `Could not load file or assembly 'System.IdentityModel, Version=4.0.0.0'` | Windows Identity Foundation isn't on .NET ([NAMESPACES.md](NAMESPACES.md#summary-of-what-doesnt-work)) |
| `Could not load file or assembly '<site>/bin/...'` | A hard-coded `~/bin`: the assemblies are in `bin_dotnet` (step 6) |
| Language keys (`[login]`) instead of text, or dates as `MM/dd/yyyy` and `¤` currency, on a server or container only | No locale: the invariant culture; set `<globalization culture uiCulture>` or `LANG` (step 6) |
| An empty log file with log4net | Log4Net.Async's appenders (step 3, "Libraries that don't run on .NET"); turn on `log4net.Internal.Debug` to see why |
| 503 right after a deploy | Another process is compiling the same site folder |
| `ReflectionTypeLoadException`: `Could not load type 'SqlGuidCaster'` | Microsoft's `System.Data.SqlClient` is in the application, not webForCore's: the application or a library it references has Microsoft's package at 4.9.0 or later (the build says so), or it is an earlier webForCore, or a build without `webForCore.Web.targets`. See "`System.Data.SqlClient`: webForCore's, or Microsoft's" (step 3) and "Scanning every loaded assembly's types" (step 6) |
