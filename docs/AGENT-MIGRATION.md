---
name: migrate-to-webforcore
description: Migrate an ASP.NET 4.x application (Web Forms, MVC 5, Web API 2, Web Pages, OWIN, SignalR 2, Identity 2) from .NET Framework/IIS to .NET 10 on Kestrel with webForCore, keeping its code, markup and web.config. Use when asked to port, migrate or run a System.Web app on .NET, Linux or Kestrel.
---

# Agent instructions: migrating an ASP.NET 4.x app to webForCore

You are migrating an existing ASP.NET 4.x application to run on .NET 10 with webForCore. webForCore provides
System.Web and Microsoft's ASP.NET libraries, under their original assembly names, so the migration is mostly
project and configuration work, not a rewrite. These instructions are self-contained. The human-oriented version is
`docs/MIGRATING.md` in the webForCore repository. Read `docs/NAMESPACES.md` and `docs/IIS-COMPATIBILITY.md` there
when a step refers to them.

## Ground rules

1. **Preserve behaviour.** The goal is the same HTTP responses as on IIS. Don't modernise, refactor, rename, reformat
   or "improve" application code. Change only what the platform requires, and keep each change minimal.
2. **Keep `web.config`**, `<system.webServer>` included; webForCore reads it. Don't move settings to
   `appsettings.json`.
3. **Don't rewrite to ASP.NET Core.** No converting pages to Razor Pages, controllers to ASP.NET Core controllers, or
   `HttpContext.Current` to dependency injection.
4. **Never weaken security to make something work.** Don't disable request validation, view state MAC, anti-forgery
   checks, authentication or authorization, and don't serve `bin_dotnet`, `App_Data` or source files.
5. **Stop and ask the user** before any of these:
   - replacing an authentication scheme (Windows authentication);
   - changing a data access technology (LINQ to SQL, EF 1–4);
   - removing a feature;
   - dropping a third-party library;
   - committing, pushing or deploying.

   Report the blocker with the files involved and the options from `docs/NAMESPACES.md`.
6. **Work in small verified steps**, building after each phase. Keep a running list of the changes you make and why;
   it goes in the final report.

## Phase 0: Assess (read only)

Do this before changing anything, and present the findings to the user.

1. Identify the project type(s) from the `.csproj`, `packages.config`, `Global.asax`, and the files: `.aspx`/`.ascx`,
   `Controllers/` + `Views/*.cshtml`, `ApiController` subclasses, `.cshtml` without controllers (Web Pages),
   `Startup.cs` with `[assembly: OwinStartup]`.
2. Search for **blockers**, and record each hit's file and line:

   | Search for | Blocker |
   | --- | --- |
   | `mode="Windows"`, `WindowsIdentity`, `WindowsPrincipal`, `<identity impersonate` | Windows authentication and impersonation: not available |
   | `.svc` files, `ServiceHost`, `<system.serviceModel><services>` | WCF hosting: not available (client proxies may work) |
   | `LinqDataSource`, `LinqBinaryModelBinder`, `MetaModel.RegisterContext` with a `DataContext` | ASP.NET's LINQ to SQL integrations: not available (LINQ to SQL itself, `System.Data.Linq`, `DataContext` and `.dbml`, works) |
   | `.sdf`, `System.Data.SqlServerCe`; a `.mdf` path or LocalDB in a `DataContext`'s connection | SQL Server Compact: not available; database files and LocalDB: Windows only |
   | `ObjectContext`, `.edmx` with EF < 6, `EntityDataSource` | EF 1–4: not available |
   | `App_WebReferences` | The build provider generates no proxy |
   | `Microsoft.Win32.Registry`, `[ComImport]`, `Marshal.GetActiveObject`, `System.Messaging`, `Microsoft.Office.Interop` | Windows only |
   | `Metafile`, `.emf`, `.wmf` | System.Drawing metafiles: not implemented |
   | `NetDataContractSerializer` (outside MVC Futures) | Not on .NET (`Html.Serialize`/`[Deserialize]` work) |
   | `AppDomain.CreateDomain`, `Thread.Abort` | Different on .NET |
   | `System.IdentityModel`, `<system.identityModel`, `WSFederationAuthenticationModule`, `SessionAuthenticationModule`, `FederatedAuthentication`; IdentityServer3, Thinktecture.IdentityModel | Windows Identity Foundation: not available (`System.Security.Claims` works). WS-Federation sign-in: Katana's `Microsoft.Owin.Security.WsFederation`; tokens: `System.IdentityModel.Tokens.Jwt` |
   | `System.Workflow`, `.xoml`, `System.Activities`, `WorkflowServiceHost` | Windows Workflow Foundation: not available. WF4 code can move to CoreWF; WF3 needs a rewrite (to WF4 for CoreWF, or plain code) |
   | `System.Data.Services` (not `.Client`), `DataService<`, `.svc` with `DataServiceHostFactory` | WCF Data Services hosting: not available (the client: `Microsoft.Data.Services.Client` 5.8.4, Phase 2 step 5) |
   | No `.csproj` for the web site (a solution `Project("{E24C65DC-...}")` web site), `CodeFile=` in `.aspx`/`.ascx`, `Bin/*.dll.refresh` | A Web Site Project: not a blocker, but Phase 2 differs (step 3) |
   | `Transaction=` in `@Page`, `System.EnterpriseServices` | COM+: not available |
   | `DataProtectionConfigurationProvider` in a `configProtectedData` section | Must be decrypted on Windows first |
3. List NuGet packages and DLL references in three groups:
   - (a) replaced by webForCore (the Microsoft ASP.NET packages in Phase 2);
   - (b) managed libraries to keep;
   - (c) .NET Framework-only binaries to test or replace.
4. Check `<httpRuntime targetFramework>`. If it's missing or below 4.5, tell the user that webForCore reproduces
   4.5+ behaviour.
5. Ask the user for a way to capture reference responses from IIS (a running instance, recorded HTTP traffic, or
   existing integration tests). If none exists, say that verification will be limited to the app's own tests and
   smoke checks.

**Report:** the project type, blockers (each with its options), the dependency groups, and a go/no-go
recommendation. Wait for the user's go-ahead if there are blockers.

## Phase 1: Get webForCore

If webForCore has a release, use its packages (they aren't on nuget.org): download them into `packages/` next to the
app's solution and add that folder as a package source in `nuget.config` (`docs/MIGRATING.md` section 1):

```bash
gh release download --repo xforcore/webForCore --pattern '*.nupkg' --dir packages   # latest release
```

The app then uses `PackageReference`s (`webForCore` and the framework packages) instead of project references, and
no `<Import>` of `webForCore.WebStack.targets`. Otherwise, or if the user asks for source, clone and build it next to
the app (or use the path the user gives):

```bash
git clone https://github.com/xforcore/webForCore ../webForCore
dotnet build ../webForCore/src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build ../webForCore/WebFormsForCore.slnx -c Release
```

On Fedora/RHEL, set `OPENSSL_ENABLE_SHA1_SIGNATURES=1` for every `dotnet` command. **Check:** both builds succeed.

## Phase 2: Convert the project

1. Copy the original project file to `<name>.csproj.netfx` (for reference; don't build it).
2. Rewrite the project as `Microsoft.NET.Sdk.Web` targeting `net10.0`, with exactly the properties, import and items
   in `docs/MIGRATING.md` section 2:
   - `OutputPath` `bin_dotnet`, with the target framework and runtime identifier not appended;
   - `GenerateDependencyFile` true;
   - `PredefinedCulturesOnly` true;
   - for MVC/Web Pages, the three Razor SDK properties false;
   - `<Import Project="<webForCore>/src/webForCore.WebStack.targets" />` (project references only);
   - `App_Code` and `bin_dotnet` removed from `Compile`.
   - `<LangVersion>`: the project's (7.3 if it set none, 13 for "latest"). C# 14's span conversions can break the
     build (`CS0029` on `ReadOnlySpan<char>`) or, in a project that builds, LINQ providers at run time (EF 6's
     `does not recognize the method ... Contains[Int32](System.ReadOnlySpan...)`, #138).
   - A `.vbproj`: `<MyType>Empty</MyType>` instead of `Windows` (`BC30002` on
     `Microsoft.VisualBasic.ApplicationServices.ApplicationBase` otherwise; `My.Computer`, `My.User` and
     `My.Application` code needs a change), the old `<Import Include>` namespaces kept as items, and
     `GenerateAssemblyInfo` false when `My Project/AssemblyInfo.vb` exists (#121).
3. Keep every `.cs` file the old project compiled (code-behind with `CodeBehind=`, controllers, models, `Global.asax.cs`).
   Use the old project's `<Compile Include>` list as the source of truth: an SDK project globs all `*.cs`, so remove
   any file the old project didn't compile.
   A **Web Site Project** has no old project: create one in the site folder with `<Compile Remove="**\*.cs" />` and
   `<Compile Include="Program.cs" />` (the template in `docs/MIGRATING.md` section 2, "A Web Site Project"), so
   `CodeFile=` code, `Global.asax` and `App_Code` stay run-time compiled. Turn `Bin/*.dll.refresh` files into
   `PackageReference`s and the projects that copy into `Bin` into `ProjectReference`s (remove their copy targets), and
   remove the DLLs in `Bin`.
   Remove design-time classes (subclasses of `System.Web.UI.Design.*`: `ExpressionEditor`, `ControlDesigner`,
   `DataSourceDesigner`) from the build: .NET has no System.Design, and attributes that name them by string keep
   working (#144).
4. Keep `Content` files as they are. Remove `packages.config`, `Properties/PublishProfiles` references to IIS, and
   the `system.codedom` compiler entries for `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` in `web.config`
   (webForCore supplies the compiler). Entries naming `Microsoft.CSharp.CSharpCodeProvider` or
   `Microsoft.VisualBasic.VBCodeProvider` in `System` can stay (webForCore compiles with its own providers and their
   options, #88). Leave everything else in `web.config` alone.
5. Replace references using the tables in `docs/MIGRATING.md` section 3: every app gets the "Always" set (with
   packages: the `webForCore` package), and then one reference per framework the app uses. Remove `<Reference Include="System.Web...">` and the Microsoft
   ASP.NET packages. Keep other packages; where a package has a .NET Standard or .NET build, use it. Then go through
   "Packages that need more than a reference" in the same section: packages that bring Microsoft's ASP.NET packages
   back (`CS0433`), `lib/net45` packages with a `build/` folder, the versions webForCore requires (`NU1605`,
   including `System.ComponentModel.Annotations` 5.0.0 with EntityFramework 6.5.1), .NET Framework assemblies that are
   packages on .NET (`CS1069` in App_Code), and Katana packages webForCore doesn't build. Every `Microsoft.NET.Sdk`
   class library that references webForCore gets `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
   (`docs/MIGRATING.md` section 2, "Class libraries that reference webForCore"); without it, its
   `Microsoft.Extensions.*` references fail with `NU1605`. Remove the references `NU1510` then reports.
   Also in that section: an app's own `Microsoft.CodeAnalysis.*` must be 5.9.0 (`NU1107`, #153);
   `System.Data.Services.Client` becomes `Microsoft.Data.Services.Client` 5.8.4 (#149); `WebMatrix.Data`/`WebMatrix.WebData`
   (`Database.Open`, `WebSecurity`) are Microsoft's `Microsoft.AspNet.WebPages.Data`/`.WebData` 3.2.9 packages (#130);
   Autofac.Configuration 3, Castle.Core 3 and NHibernate 4 need newer versions (#148). An MVC 3 or 4 app needs
   `webpages:Version` `3.0.0.0` and `using CompareAttribute = System.Web.Mvc.CompareAttribute;` where `[Compare]` is
   `CS0104` (#122), and a Razor 1 view (MVC 3, Web Pages 1) with `@` before a C# keyword in a code block
   (`@string.Join(...)`) needs `@( ... )` (#174). A prebuilt net4x library that uses `CallContext` (`TypeLoadException`
   for `System.Runtime.Remoting.Messaging.CallContext`) needs a version with a .NET Standard or .NET build: SimpleInjector
   3.1 → 3.3.2 with its ExecutionContextScoping (#178). Plug-in libraries built into their own folder need their
   packages copied there (#150).
6. Add `Program.cs` from `docs/MIGRATING.md` section 4. Include session state only if the app uses `Session`. Use
   `HandleAllRequestsWithWebForms()` unless the app serves only `.aspx`/`.asmx`/`.ashx`.
7. Search the code for what behaves differently on Linux (`docs/MIGRATING.md` section 6), and fix each hit minimally:
   - `"~/bin` or `\bin` used to load or scan assemblies: they are in `bin_dotnet/`; use `HttpRuntime.BinDirectory` or
     the app's own setting (Umbraco: `umbracoBinDirectory` = `~/bin_dotnet`).
   - `UriKind.Absolute` used to tell local URLs from others: `/path` is an absolute `file:` URI on Linux.
   - `VolumeSeparatorChar`: it is `/` on Linux; use `Path.IsPathRooted`.
   - `new Semaphore(`, `new EventWaitHandle(`, `OpenExisting(` with a name: Windows-only. Ask the user before
     replacing them (a named `Mutex` or a file lock keeps cross-process hand-over).
   - A `<globalization>` element without `culture`/`uiCulture`, in an app that depends on the server's culture: a
     server with no locale runs with the invariant culture. Ask the user which culture IIS used and set it.
   - `Log4Net.Async` in the log4net configuration: it logs nothing on .NET; point the logger at the appender it
     forwards to.
   - `DynamicDirectory` (null on .NET): use `HttpRuntime.CodegenDir`; `PrivateBinPath` and `RelativeSearchPath` (null
     on .NET): use `HttpRuntime.BinDirectory` (#139, #177).
   - Relative file names in the logging configuration (log4net's `ConfigFile` and appender `<file>`, NLog's
     `${basedir}`): they resolve against `bin_dotnet/`, and a file written there restarts the app at every write.
     Point them at the site (`../App_Data/...`) (#173).
   - `SecurityProtocolType.Ssl3`: .NET's `ServicePointManager.SecurityProtocol` setter throws for it; remove it from the
     flags (#176).
   - `"\r\n` in code that reads SQL text (LINQ to SQL's `GetCommand(...).CommandText`, `DataContext.Log`) or other text
     .NET generates: it has `\n` on Linux; use `Environment.NewLine` (#175).
   - `ResetAbort`: `Thread.ResetAbort()` throws on .NET. After `catch (ThreadAbortException)`, remove it (#143).
   - `HashAlgorithm.Create()`, `KeyedHashAlgorithm.Create()`, `HMAC.Create()`, `SymmetricAlgorithm.Create()`,
     `AsymmetricAlgorithm.Create()` with no argument: they throw on .NET. Replace each with .NET Framework's default
     (`SHA1`, `HMACSHA1`, `Aes`, `RSA` 1024), never a stronger algorithm, or stored hashes stop matching (#151).
   - `CodeDomProvider.CreateProvider`, `new CSharpCodeProvider()`, `CompileAssemblyFrom`: .NET's System.CodeDom can't
     compile. Use `WebFormsForCore.CodeDom.Compiler.CodeDomProvider`/`CSharpCodeProvider` (`docs/MIGRATING.md` section
     6, #133).
   - `{0:F}`, `{0:N}`, `ToString("F")`/`("N")` with no precision, where the page relies on two decimals: ICU's
     `en-US`/`en-GB` give three (`8.990`). Tell the user; add the precision (`F2`) if they agree (#124).

**Check:** `dotnet build -c Release` succeeds. Fix compile errors one at a time:
- A missing type that `docs/NAMESPACES.md` lists as unavailable is a blocker: stop and ask.
- A missing type from a .NET Framework-only library is a dependency problem: report it.
- Platform-only issues (for example `#if` symbols or ambiguous references) can be fixed in place.
- `CS0121: The call is ambiguous` between `System.Linq.Enumerable.DistinctBy` (or `MaxBy`, `MinBy`, `Chunk`, ...) and
  the app's own extension method: call the app's method statically around the whole chain
  (`EnumerableExtensions.DistinctBy(source.SelectMany(...), x => x.Key)`). Between two `params` overloads called with
  no arguments: pass the intended overload's empty array (`new IParameter[0]`). `<LangVersion>` doesn't help.
- `CS0104: 'KeyValuePair' is an ambiguous reference` (a type .NET added, here `System.Collections.Generic.KeyValuePair`,
  with the name of one of the app's): alias the app's in that file (`using KeyValuePair = App.Types.KeyValuePair;`, #136).

## Phase 3: Run

1. From the project folder: `dotnet bin_dotnet/<AssemblyName>.dll --urls http://127.0.0.1:5080`, run in the background.
2. Request the home page, then a page of each kind the app has (a form postback, an MVC action, a Web API call, a
   static file). The first request compiles, so allow up to a minute.
3. On errors, read the ASP.NET error page and the console log. Common causes and fixes:

   | Symptom | Fix |
   | --- | --- |
   | `System.Web ... conflicts with what is already bound` | Import `webForCore.WebStack.targets` |
   | Razor SDK errors on `.cshtml` | Turn off the three Razor properties |
   | Missing `System.Web.Extensions` / `System.Web.Services` | Add the "Always" references |
   | `FileNotFoundException` for an existing file | Path case or `\` separators in code that opens the file: fix the reference, never rename to hide it (a URL finds its file in any case, #140) |
   | `CultureNotFoundException` | Expected .NET Framework behaviour; check the culture name |
   | `PlatformNotSupportedException` | A Windows-only API: find it in `docs/NAMESPACES.md`; ask before replacing it |
   | `PlatformNotSupportedException: The named version of this synchronization primitive is not supported` | A named `Semaphore`/`EventWaitHandle` (Phase 2 step 7); ask before replacing it |
   | `Could not load file or assembly '<site>/bin/...'` | A hard-coded `~/bin`: use `HttpRuntime.BinDirectory` or `~/bin_dotnet` |
   | A redirect or "another domain" error for a local URL such as `/admin/` | `Uri.TryCreate(url, UriKind.Absolute, ...)` accepts `/path` on Linux (Phase 2 step 7) |
   | `~/...` paths not mapped, or a folder named `~` appears in the site | A `Path.VolumeSeparatorChar` check (Phase 2 step 7) |
   | `SQLExpress database file auto-creation error`, or `Cannot open database "aspnetdb"` on sign-in or registration | The SQL providers' `LocalSqlServer` (SQL Express `aspnetdb.mdf`): point it at a SQL Server and create the database with `webforcore-regsql -A` (`docs/MIGRATING.md` section 5); ask the user for the server |
   | Language keys (`[login]`) or invariant formats (`¤`, `MM/dd/yyyy`) only on some servers or in containers | No locale: the invariant culture. Set `<globalization culture uiCulture>` (ask the user which) or `LANG` |
   | log4net writes nothing | Log4Net.Async (Phase 2 step 7); `log4net.Internal.Debug` shows the `TypeLoadException` |
   | Lost logins or view state errors after a restart | Add an explicit `<machineKey>` (ask the user for keys, or generate new ones and say so) |
   | "No parameterless constructor defined" for a controller, or a DI container (Unity, Ninject, StructureMap) that was never set up | Before 0.7.0, WebActivatorEx's start methods didn't run (upgrade to 0.7.0 or later). From 0.7.0 they run when they are in a `bin` assembly; a pre-start method in `App_Code` doesn't run, as on IIS: move it to a `bin` assembly, or call it from `Application_Start` (`docs/MIGRATING.md` section 3) |
   | "The SMTP host was not specified" from the app's own mail code | .NET's `SmtpClient` ignores `<mailSettings>`: replace `new SmtpClient()` with `WebFormsForCore.Net.Configuration.NetConfiguration.CreateSmtpClient()` |
   | `Conflicting versions of ASP.NET Web Pages detected: specified version is "1.0.0.0"` | An MVC 3/4 `webpages:Version`: set `3.0.0.0` (Phase 2 step 5) |
   | ``LINQ to Entities does not recognize the method '... Contains[Int32](System.ReadOnlySpan`1[...], Int32)'`` | No `<LangVersion>`: set the project's (Phase 2 step 2) |
   | `ArgumentNullException ... (Parameter 'path')` at `DirectoryInfo..ctor` in a plug-in loader | `DynamicDirectory` is null: `HttpRuntime.CodegenDir` (Phase 2 step 7) |
   | `ArgumentNullException ... (Parameter 'path1')` at `Path.Combine` | `RelativeSearchPath` is null: `HttpRuntime.BinDirectory` (Phase 2 step 7) |
   | The app restarts again and again (its start logged repeatedly, sessions lost) | A library writes into `bin_dotnet/`, such as a log at a relative path (Phase 2 step 7) |
   | `NotSupportedException: The requested security protocol is not supported` | `SecurityProtocolType.Ssl3` (Phase 2 step 7) |
   | `Unexpected "string" keyword after "@" character` (or another C# keyword) | A Razor 1 view: wrap the expression in `@( ... )` (Phase 2 step 5) |
   | `ArgumentOutOfRangeException: startIndex ('-1')` in code that searches generated SQL or text | It looks for `\r\n` (Phase 2 step 7) |
   | `TypeLoadException` for `System.Runtime.Remoting.Messaging.CallContext` from `mscorlib`, or "Make sure that the controller has a parameterless public constructor" with it inside | A prebuilt net4x library uses `CallContext`: a version with a .NET Standard or .NET build (Phase 2 step 5) |
   | `PlatformNotSupportedException: Thread abort is not supported on this platform` | Remove `Thread.ResetAbort()` (Phase 2 step 7) |
   | `PlatformNotSupportedException: This platform does not allow the automatic selection of an algorithm` | A parameterless crypto factory (Phase 2 step 7) |
   | `PlatformNotSupportedException: Operation is not supported on this platform` from `CompileAssemblyFrom...` | System.CodeDom compilation: webForCore's provider (Phase 2 step 7) |
   | `Could not load file or assembly 'System.IdentityModel, Version=4.0.0.0'` | Windows Identity Foundation, a blocker (Phase 0): stop and ask |
   | `FileNotFoundException` for a package of a plug-in built into its own folder | Copy the plug-in's packages there (`docs/MIGRATING.md` section 2, "Plug-in libraries") |
   | `MissingMethodException: ... AppDomainSetup.get_ConfigurationFile()` or `... AppDomain.DefineDynamicAssembly(...)` | Autofac.Configuration 3, Castle.Core 3 or NHibernate 4: newer versions (Phase 2 step 5) |
   | Prices shown with three decimals (`8.990`) | ICU's `en-US`/`en-GB` `NumberDecimalDigits` (Phase 2 step 7) |

**Check:** every page type renders without errors.

## Phase 4: Verify

1. Compare against the IIS reference responses from Phase 0: status codes, important headers (`Location`,
   `Content-Type`, `Set-Cookie` names, `Cache-Control`) and bodies. Ignore `Server`, `Date` and generated IDs. Explain
   each difference: either fix it, or confirm it's a documented platform difference (`docs/NAMESPACES.md`,
   `docs/IIS-COMPATIBILITY.md`).
2. Run the app's own test projects. Migrate them to `net10.0` too if they reference the web project.
3. Check that these return 404: `/bin_dotnet/<AssemblyName>.dll`, `/web.config`, `/App_Data/`, a `.cs` file, and
   `/obj/`.
4. Exercise login and logout, a form postback, session state, file upload and download, and anything the blocker
   list touched.

## Phase 5: Deploy (only when the user asks)

Follow `docs/MIGRATING.md` section 8: the site folder with `bin_dotnet/`, run under `webforcore-host` by the user's
service manager or container runtime, one host per site folder, an explicit
`<machineKey>`, and out-of-process session state before scaling out. Don't deploy, push or commit without the user's
instruction.

## Final report

Tell the user:
- what was changed (files and reasons, from your running list);
- what was verified, and how (with numbers: pages compared, tests run and passed);
- the differences from IIS that remain, and whether each is documented;
- blockers that are still open, each with its options;
- the commands to build and run the migrated app.

Report failures plainly, with the error output. Don't call the migration complete while a page type fails or the
app's tests fail.
