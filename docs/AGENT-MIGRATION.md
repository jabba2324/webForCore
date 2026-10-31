---
name: migrate-to-webforcore
description: Migrate an ASP.NET 4.x application (Web Forms, MVC 5, Web API 2, Web Pages, OWIN, SignalR 2, Identity 2) from .NET Framework/IIS to .NET 11 on Kestrel with webForCore, keeping its code, markup and web.config. Use when asked to port, migrate or run a System.Web app on .NET, Linux or Kestrel.
---

# Agent instructions: migrating an ASP.NET 4.x app to webForCore

You are migrating an existing ASP.NET 4.x application to run on .NET 11 with webForCore. webForCore provides
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
   | `System.Data.Linq`, `DataContext`, `.dbml`, `LinqDataSource` | LINQ to SQL: not available |
   | `ObjectContext`, `.edmx` with EF < 6, `EntityDataSource` | EF 1–4: not available |
   | `App_WebReferences` | The build provider generates no proxy |
   | `Microsoft.Win32.Registry`, `[ComImport]`, `Marshal.GetActiveObject`, `System.Messaging`, `Microsoft.Office.Interop` | Windows only |
   | `Metafile`, `.emf`, `.wmf` | System.Drawing metafiles: not implemented |
   | `NetDataContractSerializer` (outside MVC Futures) | Not on .NET (`Html.Serialize`/`[Deserialize]` work) |
   | `AppDomain.CreateDomain`, `Thread.Abort`, `Thread.ResetAbort` | Different on .NET |
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
gh release download --repo jabba2324/webForCore --pattern '*.nupkg' --dir packages   # latest release
```

The app then uses `PackageReference`s (`webForCore` and the framework packages) instead of project references, and
no `<Import>` of `webForCore.WebStack.targets`. Otherwise, or if the user asks for source, clone and build it next to
the app (or use the path the user gives):

```bash
git clone https://github.com/jabba2324/webForCore ../webForCore
dotnet build ../webForCore/src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build ../webForCore/WebFormsForCore.slnx -c Release
```

On Fedora/RHEL, set `OPENSSL_ENABLE_SHA1_SIGNATURES=1` for every `dotnet` command. **Check:** both builds succeed.

## Phase 2: Convert the project

1. Copy the original project file to `<name>.csproj.netfx` (for reference; don't build it).
2. Rewrite the project as `Microsoft.NET.Sdk.Web` targeting `net11.0`, with exactly the properties, import and items
   in `docs/MIGRATING.md` section 2:
   - `OutputPath` `bin_dotnet`, with the target framework and runtime identifier not appended;
   - `GenerateDependencyFile` true;
   - `PredefinedCulturesOnly` true;
   - for MVC/Web Pages, the three Razor SDK properties false;
   - `<Import Project="<webForCore>/src/webForCore.WebStack.targets" />` (project references only);
   - `App_Code` and `bin_dotnet` removed from `Compile`.
3. Keep every `.cs` file the old project compiled (code-behind with `CodeBehind=`, controllers, models, `Global.asax.cs`).
   Use the old project's `<Compile Include>` list as the source of truth: an SDK project globs all `*.cs`, so remove
   any file the old project didn't compile.
4. Keep `Content` files as they are. Remove `packages.config`, `Properties/PublishProfiles` references to IIS, and
   the `system.codedom` compiler entries for `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` in `web.config`
   (webForCore supplies the compiler). Leave everything else in `web.config` alone.
5. Replace references using the tables in `docs/MIGRATING.md` section 3: every app gets the "Always" set (with
   packages: the `webForCore` package), and then one reference per framework the app uses. Remove `<Reference Include="System.Web...">` and the Microsoft
   ASP.NET packages. Keep other packages; where a package has a .NET Standard or .NET build, use it. Then go through
   "Packages that need more than a reference" in the same section: packages that bring Microsoft's ASP.NET packages
   back (`CS0433`), `lib/net45` packages with a `build/` folder, the versions webForCore requires (`NU1605`), .NET
   Framework assemblies that are packages on .NET (`CS1069` in App_Code), and Katana packages webForCore doesn't lift.
6. Add `Program.cs` from `docs/MIGRATING.md` section 4. Include session state only if the app uses `Session`. Use
   `HandleAllRequestsWithWebForms()` unless the app serves only `.aspx`/`.asmx`/`.ashx`.

**Check:** `dotnet build -c Release` succeeds. Fix compile errors one at a time:
- A missing type that `docs/NAMESPACES.md` lists as unavailable is a blocker: stop and ask.
- A missing type from a .NET Framework-only library is a dependency problem: report it.
- Platform-only issues (for example `#if` symbols or ambiguous references) can be fixed in place.

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
   | `FileNotFoundException`, or 404 for an existing file | Path case or `\` separators: fix the reference in code or markup, never rename to hide it |
   | `CultureNotFoundException` | Expected .NET Framework behaviour; check the culture name |
   | `PlatformNotSupportedException` | A Windows-only API: find it in `docs/NAMESPACES.md`; ask before replacing it |
   | Lost logins or view state errors after a restart | Add an explicit `<machineKey>` (ask the user for keys, or generate new ones and say so) |
   | "No parameterless constructor defined" for a controller, or a DI container (Unity, Ninject, StructureMap) that was never set up | WebActivatorEx's start methods don't run on webForCore: call them from `Application_Start` (`docs/MIGRATING.md` section 3) |
   | "The SMTP host was not specified" from the app's own mail code | .NET's `SmtpClient` ignores `<mailSettings>`: replace `new SmtpClient()` with `WebFormsForCore.Net.Configuration.NetConfiguration.CreateSmtpClient()` |

**Check:** every page type renders without errors.

## Phase 4: Verify

1. Compare against the IIS reference responses from Phase 0: status codes, important headers (`Location`,
   `Content-Type`, `Set-Cookie` names, `Cache-Control`) and bodies. Ignore `Server`, `Date` and generated IDs. Explain
   each difference: either fix it, or confirm it's a documented platform difference (`docs/NAMESPACES.md`,
   `docs/IIS-COMPATIBILITY.md`).
2. Run the app's own test projects. Migrate them to `net11.0` too if they reference the web project.
3. Check that these return 404: `/bin_dotnet/<AssemblyName>.dll`, `/web.config`, `/App_Data/`, a `.cs` file, and
   `/obj/`.
4. Exercise login and logout, a form postback, session state, file upload and download, and anything the blocker
   list touched.

## Phase 5: Deploy (only when the user asks)

Follow `docs/MIGRATING.md` section 8: the site folder with `bin_dotnet/`, a systemd unit
(`tools/deploy/webforcore@.service` in webForCore) or a container, one process per site folder, an explicit
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
