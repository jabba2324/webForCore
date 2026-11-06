| webforcore-regsql | aspnet_regsql: the SQL providers' database, session state, SQL cache dependency (#123) |
# Implemented namespaces and their caveats

This page lists, assembly by assembly, the .NET Framework and ASP.NET namespaces webForCore provides on .NET 10, and
what doesn't work in each. "Upstream" assemblies are Microsoft's open-source code, compiled for .NET from the
`external/` submodules (see [MODIFICATIONS.md](MODIFICATIONS.md)). "Ported" assemblies are WebFormsForCore's port of
the .NET Framework reference source, with webForCore's changes.

Each assembly keeps the name, and where Microsoft signed it the public key, of the assembly it replaces. Compiled
references and `web.config` type names therefore resolve without changes.

Type counts are approximate: public types found by searching the source. A caveat with a file reference was confirmed
in the code; the rest come from the test suites and gates ([TESTING.md](TESTING.md)). Why webForCore follows .NET Framework
4.8 where .NET differs is in [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#compatibility-decisions).

- [Summary of what doesn't work](#summary-of-what-doesnt-work)
- [System.Web and its companions (ported)](#systemweb-and-its-companions-ported)
- [ASP.NET MVC, Web Pages, Razor (upstream)](#aspnet-mvc-web-pages-razor-upstream)
- [ASP.NET Web API (upstream)](#aspnet-web-api-upstream)
- [OWIN / Katana (upstream)](#owin--katana-upstream)
- [SignalR 2 and ASP.NET Identity 2 (upstream)](#signalr-2-and-aspnet-identity-2-upstream)
- [System.Drawing (drawingForCore)](#systemdrawing-drawingforcore)
- [Other assemblies](#other-assemblies)

## Summary of what doesn't work

| Area | Not available | What to do instead |
| --- | --- | --- |
| Windows authentication | `<authentication mode="Windows">`, `WindowsIdentity`, impersonation, file ACL authorization | Forms, cookie or OpenID Connect authentication |
| WCF | Hosting `.svc` services; `ApplicationServicesHostFactory` | CoreWCF, or ASMX (`.asmx` works) |
| WCF Data Services | Hosting OData v1-v3 services (`System.Data.Services`, `DataService<T>`, `DataServiceHostFactory`). The client, `System.Data.Services.Client`, isn't on .NET either | The client: `Microsoft.Data.Services.Client` 5.8.4, same namespace ([MIGRATING.md](MIGRATING.md#3-references), #149). Hosting: Web API OData (`Microsoft.AspNet.OData`, which runs on webForCore) |
| Windows Identity Foundation | `System.IdentityModel` and `System.IdentityModel.Services` 4.0.0.0: `<system.identityModel>`, `<system.identityModel.services>`, `WSFederationAuthenticationModule`, `SessionAuthenticationModule`, `FederatedAuthentication`, `ClaimsAuthorizationManager`, WIF's security tokens and `System.IdentityModel.Protocols.WSTrust`, and libraries built on them (IdentityServer3: `Could not load file or assembly 'System.IdentityModel, Version=4.0.0.0'`, #119; Thinktecture.IdentityModel; IdentityServer3.AccessTokenValidation 2.x, whose `System.IdentityModel.Tokens.Jwt` 4.x is built on WIF. IdentityServer3.Contrib.AccessTokenValidation 4.x, on IdentityModel 5, runs as it is: the OpenID Connect gate tests 4.1.16). `System.Security.Claims` (`ClaimsPrincipal`, `ClaimsIdentity`) is on .NET | WS-Federation sign-in: Katana's `Microsoft.Owin.Security.WsFederation` ([MIGRATING.md](MIGRATING.md#packages-that-need-more-than-a-reference)); tokens: `Microsoft.IdentityModel.Tokens`, `System.IdentityModel.Tokens.Jwt` |
| Windows Workflow Foundation | WF3 (`System.Workflow.Runtime`, `.ComponentModel`, `.Activities`, XOML) and WF4 (`System.Activities`, `WorkflowServiceHost`): only in .NET Framework's GAC, with no .NET port from Microsoft (C1 CMS, #132) | WF4: [CoreWF](https://github.com/UiPath/CoreWF), the community port of `System.Activities`. WF3: a rewrite of the workflows, to WF4 (then CoreWF) or to plain code; CoreWF doesn't run WF3 |
| `<system.diagnostics>` in Web.config | Trace listeners, sources, switches and `<trace autoflush>` configured in Web.config or machine.config aren't applied: `Trace.Listeners` has only the default listener, and a `TraceSource` its defaults. .NET's System.Diagnostics reads configuration only through `System.Diagnostics.TraceConfiguration.Register()` in .NET's System.Configuration.ConfigurationManager, which webForCore replaces with .NET Framework's System.Configuration (which hasn't that type); machine.config declares the section as an `IgnoreSection`. Web API's `EnableSystemDiagnosticsTracing()` writes its records to `Trace` all the same | Add the listeners in code, in `Application_Start` (`Trace.Listeners.Add(new TextWriterTraceListener(...))`), or give the trace writer a `TraceSource` with them ([MIGRATING.md](MIGRATING.md#5-configuration)) |
| Run-time compilation through System.CodeDom | .NET's `CodeDomProvider.CreateProvider("c#")`, `Microsoft.CSharp.CSharpCodeProvider` and `CompileAssemblyFrom*` throw `PlatformNotSupportedException`, and read no `<system.codedom>` (#133) | webForCore's `WebFormsForCore.CodeDom.Compiler.CodeDomProvider.CreateProvider("c#")` or `CSharpCodeProvider`/`VBCodeProvider` ([MIGRATING.md](MIGRATING.md#6-code-changes)) |
| Design-time support | System.Design: `System.Web.UI.Design` (`ControlDesigner`, `ExpressionEditor`, `DataSourceDesigner`) and its editors, for an app's own designer classes (#144) | Remove them from the build; attributes that name them by string still work ([MIGRATING.md](MIGRATING.md#2-convert-the-project)) |
| Entity Framework 1–4 | `EntityDataSource` (System.Web.Entity, not built), Dynamic Data's Entity Framework model provider (`MetaModel.RegisterContext` with an `ObjectContext`), EF 1's `EntityState` in MVC's editor templates. LINQ to SQL and ASP.NET's integrations of it work: System.Data.Linq ([below](#other-assemblies), #142), `LinqDataSource`, MVC's `LinqBinaryModelBinder` and `Binary` in the hidden-input helpers and editor template, Dynamic Data's LINQ to SQL model provider | EF 6.4+ or EF Core, with `ObjectDataSource` or model binding; Dynamic Data through a `DataContext` or a `DataModelProvider` |
| SQL Server Compact, database files | LINQ to SQL's SQL Server Compact provider (`.sdf`, `System.Data.SqlServerCe.3.5`); off Windows, a `.mdf` path in `new DataContext(...)` (LINQ to SQL attaches it to `localhost\sqlexpress` as a user instance, with Windows authentication) and LocalDB | SQL Server (Express, Developer or a container) and a connection string |
| COM+ transactions | `<%@ Page Transaction=... %>`, `Transactions.InvokeTransacted` | `System.Transactions.TransactionScope` |
| Web references | `App_WebReferences` (generates nothing) | Generate the proxy with `dotnet-svcutil` and compile it into the app |
| Metafiles | EMF/WMF in System.Drawing | PNG/SVG |
| `System.Runtime.Serialization.NetDataContractSerializer` in app code | Apps that use it directly (MVC Futures' `Html.Serialize` / `[Deserialize]` work: webForCore provides their serializer) | `DataContractSerializer` with a `DataContractResolver` |
| BinaryFormatter in app code | .NET's `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter` throws (since .NET 9) | `WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter` (System.Web uses it for session and view state) |
| Windows-DPAPI-encrypted configuration | Sections encrypted on Windows with `DataProtectionConfigurationProvider` | Decrypt them on Windows; re-encrypt with `webforcore-regiis` or RSA (see [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#deployment-tools)) |
| Code Access Security, partial trust | Trust levels, CAS attributes | Ignored: everything runs in full trust. A partial `<trust level>` (Medium, ...) is accepted and runs in full trust; demands of `AspNetHostingPermission` succeed at every level (#87) |
| SignalR's Service Bus backplane | `Microsoft.AspNet.SignalR.ServiceBus`, `.ServiceBus3` (their `WindowsAzure.ServiceBus` is .NET Framework only) | The Redis or SQL Server backplane ([MIGRATING.md](MIGRATING.md#scaling-out-signalr)) |
| Registry, WMI, COM interop, MSMQ, Office | Windows only | Replace |

## System.Web and its companions (ported)

### System.Web (`src/WebFormsForCore.Web`)

| Namespace | Types (approx.) | Notes |
| --- | ---: | --- |
| `System.Web` | 107 | `HttpContext`, `HttpRequest`, `HttpResponse`, `HttpApplication`, `HttpUtility` (Microsoft's, with `<httpRuntime encoderType>`), the `*Base`/`*Wrapper` abstractions. `Server.MachineName` is `Environment.MachineName`: on Windows the computer name, as on .NET Framework (both read `GetComputerName`; Windows keeps its case); on Linux the host name up to its first dot, not upper-cased or cut to NetBIOS's 15 characters (#128) |
| `System.Web.UI` | 187 | Pages, controls, master pages, themes, view state |
| `System.Web.UI.WebControls` | 413 | All the server controls, including GridView, ListView, DetailsView, FormView, Login controls, Menu, TreeView, SqlDataSource (System.Data.SqlClient), ObjectDataSource, XmlDataSource |
| `System.Web.UI.WebControls.WebParts` | 120 | Web Parts and personalization |
| `System.Web.UI.HtmlControls`, `.Adapters`, `.WebControls.Adapters` | 74 | |
| `System.Web.Configuration` | 152 | All `<system.web>` sections; configuration paths are case-insensitive |
| `System.Web.Hosting` | 71 | `HostingEnvironment`, `VirtualPathProvider`, `ApplicationHost`, webforcore-host's workers |
| `System.Web.Compilation` | 34 | `BuildManager`; run-time compilation with Roslyn (C# and VB) |
| `System.Web.ModelBinding` | 99 | Web Forms model binding |
| `System.Web.Security` | 39 | Forms authentication, membership, roles, `MachineKey`, URL authorization |
| `System.Web.SessionState` | 22 | In-process session state (the default, as `mode="InProc"` on IIS), `mode="SQLServer"` (`SqlSessionStateStore`), custom providers and module swaps (Microsoft's Redis and async SQL providers run unchanged: [MIGRATING.md](MIGRATING.md#session-state)), and `AspNetCoreSessionProvider` (`UseAspNetCoreSessionProvider()`: out of process, in ASP.NET Core's distributed cache, for applications that used StateServer or SQLServer or scale out; values must be serializable, [MIGRATING.md](MIGRATING.md#session-state)) |
| `System.Web.Caching` | 25 | Cache, output cache, `SqlCacheDependency` |
| `System.Web.Routing` | 16 | |
| `System.Web.Management` | 44 | Health monitoring |
| `System.Web.Profile`, `.Globalization`, `.Handlers`, `.Instrumentation`, `.Mail`, `.Util`, `.WebSockets`, `.Security.AntiXss` | ~60 | |
| `Microsoft.AspNetCore.Builder` | 4 | `UseWebForms`, `WebFormsOptions`: the hosting API |
| `WebFormsForCore.Hosting.IIS` | 8 | `AddWebForms`, the IIS modules |

It also carries types .NET removed that System.Web needs: `System.Resources.ResX*` (ResX reading and writing),
`System.Runtime.Remoting.Messaging.CallContext` (built on `AsyncLocal`), `System.EnterpriseServices.TransactionOption`,
`System.Drawing.Design.UITypeEditor`. These serve code compiled against webForCore: the application's own code and
libraries rebuilt for .NET. A prebuilt .NET Framework library that uses one was compiled against the .NET Framework
assembly that had it (`mscorlib` for `CallContext`), where .NET has no such type and webForCore can't add a forward,
so the library fails with `TypeLoadException: Could not load type 'System.Runtime.Remoting.Messaging.CallContext' from
assembly 'mscorlib'` (Log4Net.Async 2.0.4, which Umbraco 7 configures by default, #95; SimpleInjector 3.1's
ExecutionContextScoping, where 3.3.2 has a .NET Standard build, #178; see
[MIGRATING.md](MIGRATING.md#3-references), "Libraries that don't run on .NET").

Caveats:
- **Classic pipeline.** The integrated pipeline itself doesn't run, but its APIs work on Kestrel (notifications,
  writable headers and server variables, `TransferRequest`, `SubStatusCode`; see
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md)). `HostingEnvironment.MaxConcurrentRequestsPerCPU` and
  `MaxConcurrentThreadsPerCPU` still throw `PlatformNotSupportedException` (`Hosting/HostingEnvironment.cs`).
- **No thread abort.** `Response.End` and `Redirect(url, true)` end the request by throwing a
  `ThreadAbortException` instance, but no thread is aborted, so code that relies on an abort interrupting a running
  thread behaves differently. The instance isn't re-raised at the end of a `catch`, so `Thread.ResetAbort()` isn't
  needed, and on .NET it throws `PlatformNotSupportedException: Thread abort is not supported on this platform.`:
  remove it (#143); code that catches the exception without rethrowing it runs on. The pipeline itself goes on as
  after an abort: the response stays buffered for `EndRequest`'s modules (forms authentication's redirect of a 401,
  #129). A request that exceeds `executionTimeout` gets its 500 on time; its code runs to the end.
- **Windows-only APIs throw `PlatformNotSupportedException`:** `Util.WorkItem.Post`, `Util.Transactions.InvokeTransacted` (page transactions),
  `HttpRequest.HttpChannelBinding`, and `IisTraceListener`/`IisTraceWebEventProvider`.
- **Typed DataSets** (`.xsd` files in App_Code) compile with the site, as on IIS: the DataSet, table and row classes
  come from the Reference Source's `System.Data.TypedDataSetGenerator`, adjusted to what Visual Studio's designer
  generates (partial classes, public table constructors and column properties, tables deriving from
  `TypedTableBase<row>` for LINQ). The TableAdapters, `QueriesTableAdapter` and `TableAdapterManager` are generated
  from the designer's annotation by webForCore's own generator (`WebFormsForCore/TypedDataSets`; Microsoft's
  System.Data.Design isn't open source): Fill and Get queries, DB-direct Insert/Update/Delete, scalar and non-query
  queries, stored procedures with output parameters, `UpdateAll` in one transaction. The provider named in the
  `.xsd` must be registered (`System.Data.SqlClient` is). Not generated: the typed tables' `GetTypedTableSchema`
  (a web service returning a typed DataTable, not a DataSet), `TableAdapterManager`'s handling of self-referencing
  tables.
- **Build providers:** `App_WebReferences` produce no proxy (`Compilation/WebReferencesBuildProvider.cs`).
- **Windows authentication**, impersonation and `FileAuthorizationModule`'s ACL checks aren't available.
- **`System.Web.Mail.SmtpMail.Send`** sends with `System.Net.Mail.SmtpClient` instead of Windows CDO
  (`WebFormsForCore/SmtpMailSender.cs`). The message's CDO fields (`smtpserver`, `smtpserverport`, `smtpusessl`,
  `smtpauthenticate` with `sendusername`/`sendpassword`, `sendusing` with a pickup directory) are honoured; without
  them, `SmtpMail.SmtpServer` and then `<system.net><mailSettings><smtp>` apply. CDO's other fields (NTLM
  authentication, proxy settings) are ignored.
- **`<system.net>`:** .NET's System.Net reads no configuration. webForCore applies `<defaultProxy>` to
  `HttpClient.DefaultProxy` at application start (HttpClient, WebClient, HttpWebRequest and SOAP client proxies use
  it), and System.Web's mail senders (PasswordRecovery, CreateUserWizard, ChangePassword, `MailWebEventProvider`,
  `SmtpMail`, Web Pages' `WebMail`) use `<mailSettings><smtp>`. An application's own `new SmtpClient()` and
  `new MailMessage()` get nothing from configuration on .NET: use
  `WebFormsForCore.Net.Configuration.NetConfiguration.CreateSmtpClient()` and `.DefaultFrom`. Not applied:
  `<connectionManagement>` (.NET doesn't limit connections per server, which is also what ASP.NET's `autoConfig`
  set), `<settings>`, `<webRequestModules>`, `<authenticationModules>`, `<requestCaching>`, and proxy
  auto-detection or scripts (WPAD, PAC).
- **`<runtime><assemblyBinding>`:** on .NET Framework the application's AppDomain had web.config as its configuration
  file, and fusion read `<runtime>` from it. webForCore applies `<probing privatePath>` (#147): its folders,
  separated by `;` and relative to the site's folder, are searched after the build output, in fusion's order
  (`name.dll`, then `name/name.dll`, in a culture's subfolder for a satellite assembly, in every folder, then the same
  with `.exe`). As in fusion, only an `<assemblyBinding>` in the `urn:schemas-microsoft-com:asm.v1` namespace counts,
  and a folder that is rooted or leads out of the site's folder is skipped. A reference without a public key token binds
  to the deployed assembly of that name whatever its version, higher or lower, as fusion doesn't check the version of
  an assembly without a strong name (#171). `<dependentAssembly>` is applied as fusion applies it (#158): the first one
  for the assembly's name, public key token and culture (no `culture` is neutral) counts. A `<bindingRedirect>`
  applies to a strong-named reference whose version is in its `oldVersion` (one version or a range), the first that
  matches, in any direction; the new version is then loaded as any reference is. A `<codeBase>` for that version is
  the only place looked in for it: a relative `href` is relative to the site's folder, a `file:` URL is a path, and an
  assembly without a strong name has one code base, whatever its version, which must be in the site's folder (it is
  skipped otherwise). An `<assemblyBinding>` whose `appliesTo` names a runtime before v4 doesn't apply. Differences
  that remain: .NET binds a reference to a version at or below the one in the application's `deps.json` to that one
  before any redirect is asked for, so a redirect *up* to a version that isn't deployed,
  which fails on .NET Framework, loads the deployed one; a reference to a version above the deployed one that no
  redirect covers fails with `FileNotFoundException` where fusion threw `FileLoadException`; and an `http:` code base,
  which fusion downloaded, isn't loaded.
  `<publisherPolicy>` and `<qualifyAssembly>` are moot on .NET (no GAC publisher policy; partial names load by simple
  name). The rest of `<runtime>` isn't read: .NET takes runtime settings from `runtimeconfig.json`.
- **What .NET Framework's machine had:** .NET Framework has every Windows code page and registers SqlClient, OleDb and
  ODBC in machine.config; .NET has neither. System.Web registers `CodePagesEncodingProvider` (so
  `requestEncoding="windows-1252"` works), reads files without a BOM in the ANSI code page when `fileEncoding` isn't
  set, as .NET Framework's `Encoding.Default` did (#90, [MIGRATING.md](MIGRATING.md#6-code-changes)), and, by type name and only when not already registered, the
  `System.Data.SqlClient`, `Microsoft.Data.SqlClient`, `System.Data.OleDb` and `System.Data.Odbc` factories, when it
  loads (`WebFormsForCore/FrameworkDefaults.cs`). The provider's own package must still be deployed.
- `<system.data><DbProviderFactories>` in the app's configuration (`<add>`, `<remove>`, `<clear>`) registers ADO.NET
  providers at application start, as on .NET Framework (`WebFormsForCore/DbProviderFactoriesSectionHandler.cs`).
- `HttpUtility` follows .NET Framework 4.5+ (strict decoding); the 4.0 compatibility quirks of apps without
  `targetFramework` aren't emulated.
- A `.resx` file reference to an icon (`ResXFileRef` of type `Icon`) throws off Windows.
- `ApplicationHost.CreateApplicationHost` creates the host in the current process (.NET has one AppDomain), without
  remoting: once per process, since a process has one hosting environment ("Only one HostingEnvironment is allowed in
  an AppDomain" the second time), and that environment stays for the life of the process.
- **`AppDomain.CurrentDomain.BaseDirectory` is the build output (`bin_dotnet/`), not the site.** On IIS each
  application ran in its own AppDomain whose `ApplicationBase` was the site's folder (with `bin` as its private bin
  path), so `BaseDirectory` and `AppContext.BaseDirectory` were the site root. .NET has one AppDomain per process, whose
  base directory is the entry assembly's folder, and .NET libraries rely on it being the build output. Code that reads files next to the site through it should use
  `HttpRuntime.AppDomainAppPath`, `HostingEnvironment.ApplicationPhysicalPath` or `Server.MapPath("~/...")`, which are
  the site root as on IIS (#3). `AppDomain.CurrentDomain.SetupInformation.ApplicationBase` is the same as `BaseDirectory`.
  Libraries resolve their relative paths against it too (log4net's `ConfigFile` and appender `<file>`, NLog's
  `${basedir}`), and `bin_dotnet` is the application's `bin`: a log written there restarts the application at every
  line (#173, [MIGRATING.md](MIGRATING.md#6-code-changes)).
- **`AppDomain.CurrentDomain.DynamicDirectory` is null** (.NET has no dynamic base). On IIS it was the code generation
  folder: use `HttpRuntime.CodegenDir`, which webForCore's assembly resolver probes, as plug-in loaders that copy
  assemblies there need (#139). `AppDomainSetup.PrivateBinPath` doesn't exist and
  `AppDomain.CurrentDomain.RelativeSearchPath` (the application's `bin` on IIS) is null (#177): use
  `HttpRuntime.BinDirectory`.
- **`AppDomain.CurrentDomain.GetData("DataDirectory")`** is the site's `App_Data`, as on IIS, for data providers that
  expand `|DataDirectory|` (#50).
- **Behaviour .NET changes everywhere:** `ArgumentException.Message` ends with `(Parameter 'name')`; ICU cultures
  (set `<PredefinedCulturesOnly>true` to keep .NET Framework's `CultureNotFoundException`); `\n` newlines; paths are
  case-sensitive and `\` isn't a separator.
  The parameterless crypto factories (`HashAlgorithm.Create()`, `KeyedHashAlgorithm.Create()`, `HMAC.Create()`,
  `SymmetricAlgorithm.Create()`, `AsymmetricAlgorithm.Create()`) throw `PlatformNotSupportedException` instead of
  returning machine.config's defaults (#151, [MIGRATING.md](MIGRATING.md#6-code-changes)).

### System.Web.Extensions (`src/WebFormsForCore.Web.Extensions`)

`System.Web.UI` (ScriptManager, UpdatePanel, Timer, extenders), `System.Web.UI.WebControls` (ListView, DataPager,
QueryExtender and others), `System.Web.Script.Serialization` (`JavaScriptSerializer`), `System.Web.Script.Services`
(script services, page methods), `System.Web.ApplicationServices`, `System.Web.ClientServices`,
`System.Web.Configuration`, `System.Web.Query.Dynamic`, and the MS AJAX client scripts.

Caveats: `LinqDataSource` works over LINQ to SQL's `DataContext` (System.Data.Linq, #142), as on .NET Framework: select with
`Where`, `OrderBy`, `GroupBy`, `Select`, `AutoGenerateWhereClause`, paging and sorting by the data source, and update, insert and
delete with optimistic concurrency (a `rowversion`, kept in view state as a `Binary`). UpdatePanel's partial rendering depends on the browser definitions, as on IIS: a client whose User-Agent
matches no definition gets full postbacks. `ApplicationServicesHostFactory` (WCF) isn't available. WCF service references are built with CoreWCF
primitives.

### System.Web.Services (`src/WebFormsForCore.Web.Services`)

`System.Web.Services`, `.Configuration`, `.Description`, `.Discovery`, `.Protocols`: ASMX web services and SOAP
client proxies. SOAP 1.1 and 1.2, faults, HTTP POST from the local machine, `?WSDL` and `[ScriptService]` JSON are
covered by the IIS gate. XML serialization uses .NET's `XmlSerializer`. The HTTP/MIME
WSDL importers are .NET Framework only; importing WSDL that uses HTTP bindings is untested.

Caveat: the page a browser gets for a `.asmx` (and `?op=<name>`) is webForCore's own `DefaultWsdlHelpGenerator.aspx`
(MIT): .NET Framework's isn't open source. It lists the operations with their documentation, links the service
description and, for local requests, has an HTTP POST test form; it doesn't show SOAP request/response samples or WS-I
conformance warnings. An app's own `<wsdlHelpGenerator href>` still replaces it.

### System.Configuration (`src/WebFormsForCore.Configuration`)

Assembly `System.Configuration.ConfigurationManager`, which replaces both the NuGet package and .NET Framework's
System.Configuration. Namespaces `System.Configuration` (`ConfigurationManager`, sections, providers, protected
configuration, CodeDom compiler configuration), `.Internal`, `.Provider`, and `System.Net.Configuration`.

The sources are Microsoft's Reference Source (System.Configuration.dll and the configuration parts of System.dll).

Caveats: `<system.net>` sections (`SmtpSection`, `DefaultProxySection`, ...) can be read with `ConfigurationManager.GetSection`, but .NET's System.Net doesn't apply them. `WebFormsForCore.Net.Configuration.NetConfiguration` applies `<defaultProxy>` (`ApplyDefaultProxy`, which System.Web calls at start) and `<mailSettings>` (`CreateSmtpClient`, `ApplyMailSettings`, `DefaultFrom`); see System.Web's caveats for what isn't applied. `DataProtectionConfigurationProvider` uses webForCore's DPAPI emulation, so a section Windows DPAPI encrypted
can't be read on Linux (the error says so). `RsaProtectedConfigurationProvider` keeps key containers as XML files in
the key directory; containers exported with `aspnet_regiis -px` can be imported. The key directory is
`$WEBFORCORE_KEYS`, `/var/lib/webforcore/keys` or `~/.webforcore/keys`.

webForCore ships only Microsoft assemblies. WebActivatorEx runs as it is (from 0.7.0), and AjaxControlToolkit
isn't provided ([MIGRATING.md](MIGRATING.md#3-references)).

### Smaller companions

| Assembly | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.ApplicationServices | `System.Web.Security` (MembershipProvider, RoleProvider, ...), `System.Web.Configuration`, `System.Web.Hosting` | System.Web forwards the membership types here, as on .NET Framework |
| System.Web.DataVisualization | `System.Web.UI.DataVisualization.Charting` (Chart control) | Renders through the SkiaSharp System.Drawing |
| System.Web.Mobile | `System.Web.Mobile`, `System.Web.UI.MobileControls` (+ `.Adapters`) | Designers are .NET Framework only. `FontInfo.Name`'s converter reads the installed font list once (.NET Framework refreshed it on `SystemEvents.InstalledFontsChanged`, which is Windows only) |
| System.Web.RegularExpressions | `System.Web.RegularExpressions` (`NetCoreRegexes`) | The classic public regex classes (`TagRegex`, `DirectiveRegex`, ...) are .NET Framework only; on .NET, System.Web's parser uses source-generated regexes. Code that uses the classic classes directly won't compile |
| Microsoft.Web.Infrastructure | `Microsoft.Web.Infrastructure`, `.DynamicModuleHelper`, `.DynamicValidationHelper` | webForCore's implementation of the documented API: `DynamicModuleUtility.RegisterModule` (on `HttpApplication.RegisterModule`), `HttpContextHelper`, `InfrastructureHelper`, `ValidationUtility` (granular request validation) |
| System.Web.Abstractions, System.Web.Routing (4.0.0.0) | (type forwarders) | The .NET Framework 4 facades: `HttpContextBase` and the other abstractions, and routing, are in System.Web, and these forward there, as in the GAC. Part of the "Always" set, for web.configs from MVC 2-4 projects that list them in `<compilation><assemblies>` and .NET 3.5 libraries that reference them |
| System.Web.Optimization, WebGrease | `System.Web.Optimization`, `Microsoft.AspNet.Web.Optimization.WebForms`, `WebGrease`, `Microsoft.Ajax.Utilities` | Bundling and minification. Microsoft's identities (System.Web.Optimization and Microsoft.AspNet.Web.Optimization.WebForms 1.1.0.0, WebGrease 1.6.5135.21930, PublicKeyToken `31bf3856ad364e35`), so libraries built on Microsoft's bundling (BundleTransformer) compile and bind against them (#141) |
| System.Web.DynamicData | `System.Web.DynamicData` (+ `.ModelProviders`), and `System.ComponentModel.DataAnnotations.ScaffoldTableAttribute`, which .NET dropped | Part of the "Always" set: System.Web loads it for every data control with `ItemType`. The LINQ to SQL model provider works (`RegisterContext` with a `DataContext`, #142; `LinqDataSource` gets its context from the table and `LoadWithForeignKeys`); the Entity Framework 1–4 one isn't built (`RegisterContext` with an `ObjectContext`), nor are the EF 5 spatial types (`DbGeography`, `DbGeometry`) as column types. Designers are .NET Framework only |
| System.Web.Entity | | Not built by the solution |

## ASP.NET MVC, Web Pages, Razor (upstream)

| Assembly (version) | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.Mvc 5.3 | `System.Web.Mvc` (~250 types), `.Ajax`, `.Async`, `.Filters`, `.Html`, `.Razor`, `.Routing`, `.Routing.Constraints` | `LinqBinaryModelBinder` and `Binary` in the hidden-input helpers and editor template are built on System.Data.Linq (#142); EF 1's `EntityState` check is compiled out. Views are compiled at run time: turn off the ASP.NET Core Razor SDK in the project |
| Microsoft.Web.Mvc (MVC Futures) | `Microsoft.Web.Mvc`, `.Controls`, `.ModelBinding`, `.Resources` | `Html.Serialize` / `[Deserialize]` work: `MvcSerializer` uses webForCore's replacement for NetDataContractSerializer (type-preserving `DataContractSerializer`, machine-key signed and encrypted as before). The hidden-field format differs from .NET Framework's, so a value serialized by a .NET Framework server can't be read back |
| System.Web.Razor 3.0 | `System.Web.Razor`, `.Generator`, `.Parser`, `.Text`, `.Tokenizer`, `.Editor` | Generated code uses `\n` on Linux. As in Razor 2, `@` before a C# keyword inside a code block (`@string.Join(...)`), which Razor 1 accepted, is a parser error: wrap it in `@( ... )` (#174, [MIGRATING.md](MIGRATING.md#before-you-start)) |
| System.Web.WebPages 3.0 | `System.Web.WebPages`, `.Html`, `.Scope`, `.Instrumentation`, `System.Web.Helpers` (AntiForgery) | Side-by-side Web Pages versions aren't supported |
| System.Web.WebPages.Razor 3.0 | `System.Web.WebPages.Razor`, `.Configuration` | |
| System.Web.WebPages.Deployment 3.0 | `System.Web.WebPages.Deployment` | Dependency checks use a collectible load context instead of an AppDomain |
| System.Web.Helpers 3.0 | `System.Web.Helpers` (WebGrid, Chart, WebImage, Crypto, Json) | Chart and WebImage render through the SkiaSharp System.Drawing |
| WebMatrix.Data, WebMatrix.WebData, System.Web.WebPages.Administration | | Not built. Microsoft's `Microsoft.AspNet.WebPages.Data` and `.WebData` 3.2.9 packages run as they are (`Database`, `WebSecurity`, `SimpleMembershipProvider`; [MIGRATING.md](MIGRATING.md#3-references), #130) |

## ASP.NET Web API (upstream)

| Assembly (version) | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.Http 5.3 | `System.Web.Http` and `.Batch`, `.Controllers`, `.Dependencies`, `.Description`, `.Dispatcher`, `.ExceptionHandling`, `.Filters`, `.Hosting`, `.Metadata`, `.ModelBinding`, `.Results`, `.Routing`, `.Services`, `.Tracing`, `.Validation`, `.ValueProviders` | Model-state messages built from exceptions use .NET's `ArgumentException` format |
| System.Web.Http.WebHost 5.3 | `System.Web.Http.WebHost` | |
| System.Web.Http.Owin 5.3 | `System.Web.Http.Owin` | |
| System.Web.Http.Cors, System.Web.Cors 5.3 | `System.Web.Http.Cors`, `System.Web.Cors` | |
| System.Web.Http.Tracing 5.3 | `System.Web.Http.Tracing` (`SystemDiagnosticsTraceWriter`), and `System.Web.Http`'s `HttpConfigurationTracingExtensions` (`config.EnableSystemDiagnosticsTracing()`) | Package `webForCore.Web.Http.Tracing` (Microsoft.AspNet.WebApi.Tracing 5.3.0). The records go through `System.Diagnostics.Trace`, or the `TraceSource` the writer is given, as on .NET Framework, but Web.config's `<system.diagnostics>` isn't applied, so its listeners aren't there ([summary](#summary-of-what-doesnt-work)): add them in code |
| System.Net.Http.Formatting 6.0 | `System.Net.Http`, `.Formatting`, `.Handlers`, `.Headers` | `ContentDispositionHeaderValue.FileName` is unquoted by .NET (Web API's providers handle this); indented JSON and XML use `\n` |

## OWIN / Katana (upstream)

Microsoft.Owin 4.2.3 and companions: `Microsoft.Owin` (+ `.Builder`, `.Extensions`, `.Infrastructure`, `.Logging`,
`.Mapping`, `.Security`), `Microsoft.Owin.Security` (+ `.Cookies`, `.OAuth`, `.DataHandler`, `.DataProtection`),
`Microsoft.Owin.Security.OpenIdConnect`, `.Jwt`, `.WsFederation`, `.ActiveDirectory` (Entra ID / Azure AD and ADFS
bearer tokens), `.Google`, `.Facebook`, `.MicrosoftAccount` and `.Twitter`, `Microsoft.Owin.Host.SystemWeb`,
`Microsoft.Owin.Hosting`, `Microsoft.Owin.Host.HttpListener`, `Microsoft.Owin.Testing`, `Microsoft.Owin.Cors`.
`Owin.dll` (`IAppBuilder`) is the `Owin` 1.0 package. Each is the `webForCore.<assembly>` package.

Caveats: `DpapiDataProtectionProvider` uses the DPAPI emulation, so its payloads can't be exchanged with Windows. On
System.Web the machine key protects cookies, as on IIS. The "Domain" hosting starter throws (it needs AppDomains). The
HttpListener host uses Katana's Mono code paths. `HostString` accepts hosts that mix Unicode and punycode labels.

OpenID Connect, JWT, WS-Federation and Active Directory are built against IdentityModel 8 (`Microsoft.IdentityModel.*`
and `System.IdentityModel.Tokens.Jwt` 8.19.1), the version CoreWCF brings to every webForCore application, where
Microsoft's packages use 5.3; the source is unchanged ([MODIFICATIONS.md](MODIFICATIONS.md#library-changes-by-kind)
lists the APIs it uses). An application's own IdentityModel code compiles against 8.x too. What differs from IIS:
the authorize and end-session redirects carry IdentityModel's telemetry for 8.x (`x-client-SKU=ID_NET10_0`,
`x-client-ver=8.19.1.0`), and IdentityModel's exception messages are worded as in 8.x; the exception types an
`AuthenticationFailed` notification sees are the same. The OpenID Connect gate (`test/webForCore.Oidc.Gate.Tests`)
signs in to an application configured as for Entra ID, and CI compares it with IIS 10
([IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#openid-connect)).

## SignalR 2 and ASP.NET Identity 2 (upstream)

| Assembly | Namespaces | Caveats |
| --- | --- | --- |
| Microsoft.AspNet.SignalR.Core 2.4.3 | `Microsoft.AspNet.SignalR` (+ `.Hubs`, `.Hosting`, `.Infrastructure`, `.Messaging`, `.Transports`, `.Json`, `.Owin`, `.Tracing`) | Scale-out backplanes: the three packages below. The Service Bus ones (`Microsoft.AspNet.SignalR.ServiceBus`, `.ServiceBus3`) aren't built: `WindowsAzure.ServiceBus`, which they are written on, is .NET Framework only (every version, to 7.0.1, is built on WCF's `System.ServiceModel`), and Azure's .NET clients (`Azure.Messaging.ServiceBus`) have another API. Use Redis or SQL Server ([MIGRATING.md](MIGRATING.md#scaling-out-signalr)) |
| Microsoft.AspNet.SignalR.SystemWeb 2.4.3 | `Microsoft.AspNet.SignalR`, `System.Web.Routing` | The obsolete `MapConnection`/`MapHubs` throw, as upstream; use `MapSignalR` |
| Microsoft.AspNet.SignalR.Client 2.4.3 | `Microsoft.AspNet.SignalR.Client` (+ `.Hubs`, `.Http`, `.Transports`) | The JavaScript client is the app's own script, unchanged |
| Microsoft.AspNet.SignalR.StackExchangeRedis 2.4.3 | `Microsoft.AspNet.SignalR` (`UseStackExchangeRedis`, `RedisScaleoutConfiguration`), `.StackExchangeRedis` | The Redis backplane on StackExchange.Redis 2 (a NuGet dependency, 2.9.32; Microsoft's package asks for 2.0.513 or later). As in Microsoft's 2.4.3, its error messages are missing from the assembly (its `Resources` class looks for the Redis project's), so where one would be read a `MissingManifestResourceException` is thrown |
| Microsoft.AspNet.SignalR.Redis 2.4.3 | `Microsoft.AspNet.SignalR` (`UseRedis`, `RedisScaleoutConfiguration`), `.Redis` | The Redis backplane on StackExchange.Redis.StrongName 1.x (a NuGet dependency, 1.2.6, its .NET Standard 1.5 build; Microsoft's package asks for 1.1.608 or later). Its `RedisScaleoutConfiguration` has the full name of StackExchangeRedis's: an application references one of the two |
| Microsoft.AspNet.SignalR.SqlServer 2.4.3 | `Microsoft.AspNet.SignalR` (`UseSqlServer`, `SqlScaleoutConfiguration`), `.SqlServer` | The SQL Server backplane, on webForCore's System.Data.SqlClient. It creates its `[SignalR]` schema and tables at start; with Service Broker on in the database (`ALTER DATABASE ... SET ENABLE_BROKER`) it is notified of new messages (`SqlDependency`), otherwise it polls, as on .NET Framework. SQL Server, not Azure SQL Database (as upstream) |
| Microsoft.AspNet.Identity.Core / .EntityFramework / .Owin 2 | `Microsoft.AspNet.Identity`, `.EntityFramework`, `.Owin` | On Entity Framework 6.5; LocalDB is Windows only, so use SQL Server or another provider. Token providers use the DPAPI emulation |

## System.Drawing (drawingForCore)

System.Drawing.Common 6.0 (Microsoft's managed code) over a GDI+ engine written on SkiaSharp, from
[drawingForCore](https://github.com/xforcore/drawingForCore)'s packages (`drawingForCore`, `drawingForCore.Common`),
which the `webForCore` package depends on. Namespaces `System.Drawing`, `.Drawing2D`, `.Imaging`, `.Printing`, `.Text`,
`.Design`. The `System.Drawing` assembly forwards .NET Framework's System.Drawing types to it. drawingForCore's README
has the full list of what works.

Caveats:
- **Not implemented:** metafiles (EMF/WMF); multi-frame saving (`SaveAdd`); `GetEncoderParameterList`; graphics on
  an HDC, HWND or screen (`FromHdc`, `FromHwnd`, `CopyFromScreen`); most printing; `Font.FromHdc`;
  `GetHalftonePalette`; pen compound arrays; ICC profiles; JPEG-compressed TIFF.
- **Text:** no font fallback for missing glyphs, no right-to-left or vertical layout. Windows font names map to
  metric-compatible fonts (Liberation, Carlito, Caladea) through fontconfig.
- Output is close to Windows GDI+ but not pixel-identical.
- Microsoft's own suite: 3,705 of 3,882 pass in drawingForCore's CI on Linux and Windows, and 141 of the 151
  failures are metafiles (see drawingForCore's
  [TESTING.md](https://github.com/xforcore/drawingForCore/blob/main/docs/TESTING.md)).

## Other assemblies

| Assembly | Purpose |
| --- | --- |
| WebFormsForCore.Serialization.Formatters | A port of BinaryFormatter (and SoapFormatter) in the `WebFormsForCore.Serialization.Formatters.Binary` namespace, with surrogates for .NET Framework types .NET no longer serializes: `NameObjectCollectionBase` and its subclasses (`NameValueCollection`, `HttpCookie.Values`), `CaseInsensitiveComparer`, `CaseInsensitiveHashCodeProvider`, `TextInfo` and `CultureInfo`, written with .NET Framework's members; and `Type`, `MethodInfo`, `ConstructorInfo`, `FieldInfo`, `PropertyInfo`, `EventInfo`, `Missing`, `Assembly` and `Module`, written as .NET Framework's `UnitySerializationHolder` and `MemberInfoSerializationHolder` and read from them (#156; `DBNull` already was). A type is written with the assembly .NET Framework had it in (from `TypeForwardedFrom`, or the .NET Framework facade .NET forwards it from: `Enumerable` in System.Core); one .NET Framework never had (`System.Text.Json`'s) keeps .NET's assembly name; `System.Empty` in a .NET Framework stream (COM interop's VT_EMPTY, which .NET doesn't have) throws `SerializationException`; delegates are still not serializable, as on .NET. webForCore's .NET Framework 4.0 assemblies are written as `Version=4.0.0.0`, and exceptions from deserialization aren't wrapped in a `SerializationException`, as on .NET Framework. System.Web uses it for out-of-process session state, view state and the output cache. Not handled: subclasses of `NameObjectCollectionBase` that override `GetObjectData` or `OnDeserialization`, and field initializers of derived types (no serialization constructor runs) |
| System.Net.Http.WebRequest (4.0.0.0) | .NET Framework's `WebRequestHandler`, written from its documented API as an `HttpClientHandler`, for libraries built against it: Katana's social providers and WS-Federation create one as their back-channel handler (#18). Part of the "Always" set. `ServerCertificateValidationCallback` is passed to `ServerCertificateCustomValidationCallback` (its sender is the `HttpRequestMessage`); the settings only `HttpWebRequest` had (`AllowPipelining`, `CachePolicy`, `ReadWriteTimeout`, `ContinueTimeout`, ...) are kept but have no effect |
| System.Data.SqlClient (4.6.1.6, upstream) | Microsoft's System.Data.SqlClient 4.9.1, built from `external/maintenance-packages` (dotnet/maintenance-packages) with one change: `Assembly.GetTypes()` works (#41). Microsoft's packages for .NET, 4.4.3 to 4.9.1, have a type .NET 8 and later can't load (`SqlGuidCaster`), so scanning every loaded assembly's types threw a `ReflectionTypeLoadException`. Its public API is Microsoft's 4.9.1, with the windows and unix builds of Microsoft's package. Package `webForCore.Data.SqlClient`, which `webForCore.Web` depends on. One of the two is in a build (`webForCore.Web.targets`, #195): Microsoft's `System.Data.SqlClient` package when the build has it at 4.9.0 or later (an application that references Microsoft's supported package gets it, and everything in webForCore runs on it), otherwise this one, and the older Microsoft packages Entity Framework 6 and others bring are left out ([MIGRATING.md](MIGRATING.md#3-references)) |
| System.Data.Linq (4.0.0.0, upstream) | LINQ to SQL: Microsoft's .NET Framework 4.8 source (`external/referencesource`), built for .NET on webForCore's System.Data.SqlClient (#142). `DataContext`, `Table<T>`, attribute and XML mapping, `EntitySet`/`EntityRef`, change tracking and optimistic concurrency, `CompiledQuery`, `DataLoadOptions`, stored procedures and functions, `Binary`, `SqlMethods`, `ExecuteQuery`/`ExecuteCommand`, `System.Transactions`. Its errors are .NET Framework's: Microsoft's build generated the classes that create them, which referencesource doesn't include, and webForCore generates them from data read from .NET Framework 4.8's assembly, so each error has .NET Framework's exception type and message. .NET Framework's identity (the ECMA key, PublicKeyToken `b77a5c561934e089`); references to 3.5.0.0 bind to it. Package `webForCore.Data.Linq`, part of the "Always" set (the `webForCore` package), as it is in .NET Framework's GAC. Caveats: SQL Server only, with the limits in the summary above (no SQL Server Compact; `.mdf` paths and LocalDB only on Windows); the SQL it generates has the platform's newline (`\n` on Linux), in `DataContext.Log` and in `GetCommand(query).CommandText`, as `SqlFormatter` writes it with `AppendLine` (#175); `DBConvert`'s conversions through `byte[]` and `Binary` use webForCore's BinaryFormatter port; the debug build's capture of compiled readers to an assembly file is off. `LinqDataSource`, MVC's `Binary` model binding (`LinqBinaryModelBinder`) and Dynamic Data's LINQ to SQL model provider are built on it. Web API OData's model builder (`Microsoft.AspNet.OData`), which maps `Binary`, needs it (#120) |
| WebFormsForCore.Compilers | Roslyn `csc`/`vbc` for run-time compilation |
| aspnetcore_compiler | aspnet_compiler for .NET (precompiled sites) |
| webforcore-regiis | aspnet_regiis's protected-configuration commands |
