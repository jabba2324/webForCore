# Implemented namespaces and their caveats

This page lists, assembly by assembly, the .NET Framework and ASP.NET namespaces webForCore provides on .NET 11, and
what doesn't work in each. "Lifted" assemblies are Microsoft's open-source code, copied and built for .NET (see
[MODIFICATIONS.md](MODIFICATIONS.md)). "Ported" assemblies are WebFormsForCore's port of the .NET Framework reference
source, with webForCore's changes.

Each assembly keeps the name, and where Microsoft signed it the public key, of the assembly it replaces. Compiled
references and `web.config` type names therefore resolve without changes.

Type counts are approximate: public types found by searching the source. A caveat with a file reference was confirmed
in the code; the rest are from `LIFT.md`.

- [Summary of what doesn't work](#summary-of-what-doesnt-work)
- [System.Web and its companions (ported)](#systemweb-and-its-companions-ported)
- [ASP.NET MVC, Web Pages, Razor (lifted)](#aspnet-mvc-web-pages-razor-lifted)
- [ASP.NET Web API (lifted)](#aspnet-web-api-lifted)
- [OWIN / Katana (lifted)](#owin--katana-lifted)
- [SignalR 2 and ASP.NET Identity 2 (lifted)](#signalr-2-and-aspnet-identity-2-lifted)
- [System.Drawing (lifted, new engine)](#systemdrawing-lifted-new-engine)
- [Other assemblies](#other-assemblies)

## Summary of what doesn't work

| Area | Not available | What to do instead |
| --- | --- | --- |
| Windows authentication | `<authentication mode="Windows">`, `WindowsIdentity`, impersonation, file ACL authorization | Forms, cookie or OpenID Connect authentication |
| WCF | Hosting `.svc` services; `ApplicationServicesHostFactory` | CoreWCF, or ASMX (`.asmx` works) |
| LINQ to SQL, Entity Framework 1–4 | `LinqDataSource`, MVC's `LinqBinaryModelBinder`, the `Binary` editor templates, `EntityDataSource` (not built), the DynamicData LINQ to SQL provider | EF 6.4+ or EF Core, with `ObjectDataSource`/model binding |
| Old mail API | `System.Web.Mail.SmtpMail.Send` (needs Windows CDO) | `System.Net.Mail.SmtpClient` or a mail library |
| COM+ transactions | `<%@ Page Transaction=... %>`, `Transactions.InvokeTransacted` | `System.Transactions.TransactionScope` |
| Typed DataSets in App_Code | `.xsd` build provider (throws at compile time) | Generate the DataSet class at design time and compile it into the app |
| Web references | `App_WebReferences` (generates nothing) | Generate the proxy with `dotnet-svcutil` and compile it into the app |
| Metafiles | EMF/WMF in System.Drawing | PNG/SVG |
| NetDataContractSerializer | MVC Futures `Html.Serialize` / `[Deserialize]` | Another serializer |
| BinaryFormatter in app code | .NET 11's `System.Runtime.Serialization.Formatters.Binary.BinaryFormatter` throws | `WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter` (System.Web uses it for session and view state) |
| Windows-DPAPI-encrypted configuration | Sections encrypted on Windows with `DataProtectionConfigurationProvider` | Decrypt them on Windows; re-encrypt with `webforcore-regiis` or RSA (see [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md#deployment-tools)) |
| Code Access Security, partial trust | Trust levels, CAS attributes | Ignored: everything runs in full trust |
| Registry, WMI, COM interop, MSMQ, Office | Windows only | Replace |

## System.Web and its companions (ported)

### System.Web (`src/WebFormsForCore.Web`)

| Namespace | Types (approx.) | Notes |
| --- | ---: | --- |
| `System.Web` | 107 | `HttpContext`, `HttpRequest`, `HttpResponse`, `HttpApplication`, `HttpUtility` (Microsoft's, with `<httpRuntime encoderType>`), the `*Base`/`*Wrapper` abstractions |
| `System.Web.UI` | 187 | Pages, controls, master pages, themes, view state |
| `System.Web.UI.WebControls` | 413 | All the server controls, including GridView, ListView, DetailsView, FormView, Login controls, Menu, TreeView, SqlDataSource (System.Data.SqlClient), ObjectDataSource, XmlDataSource |
| `System.Web.UI.WebControls.WebParts` | 120 | Web Parts and personalization |
| `System.Web.UI.HtmlControls`, `.Adapters`, `.WebControls.Adapters` | 74 | |
| `System.Web.Configuration` | 152 | All `<system.web>` sections; configuration paths are case-insensitive |
| `System.Web.Hosting` | 71 | `HostingEnvironment`, `VirtualPathProvider`, `ApplicationHost`, the PseudoAppDomain |
| `System.Web.Compilation` | 34 | `BuildManager`; run-time compilation with Roslyn (C# and VB) |
| `System.Web.ModelBinding` | 99 | Web Forms model binding |
| `System.Web.Security` | 39 | Forms authentication, membership, roles, `MachineKey`, URL authorization |
| `System.Web.SessionState` | 22 | In-process and ASP.NET Core session providers |
| `System.Web.Caching` | 25 | Cache, output cache, `SqlCacheDependency` |
| `System.Web.Routing` | 16 | |
| `System.Web.Management` | 44 | Health monitoring |
| `System.Web.Profile`, `.Globalization`, `.Handlers`, `.Instrumentation`, `.Mail`, `.Util`, `.WebSockets`, `.Security.AntiXss` | ~60 | |
| `Microsoft.AspNetCore.Builder` | 4 | `UseWebForms`, `WebFormsOptions`: the hosting API |
| `WebFormsForCore.Hosting.IIS` | 8 | `AddWebForms`, the IIS modules |

It also carries types .NET removed that System.Web needs: `System.Resources.ResX*` (ResX reading and writing),
`System.Runtime.Remoting.Messaging.CallContext` (built on `AsyncLocal`), `System.EnterpriseServices.TransactionOption`,
`System.Drawing.Design.UITypeEditor`.

Caveats:
- **Classic pipeline.** The integrated pipeline itself doesn't run, but its APIs work on Kestrel (notifications,
  writable headers and server variables, `TransferRequest`, `SubStatusCode`; see
  [IIS-COMPATIBILITY.md](IIS-COMPATIBILITY.md)). `HostingEnvironment.MaxConcurrentRequestsPerCPU` and
  `MaxConcurrentThreadsPerCPU` still throw `PlatformNotSupportedException` (`Hosting/HostingEnvironment.cs`).
- **No thread abort.** `Response.End` and `Redirect(url, true)` end the request by throwing a
  `ThreadAbortException` instance, but no thread is aborted, so code that relies on `Thread.ResetAbort` or on an abort
  interrupting a running thread behaves differently. A request that exceeds `executionTimeout` gets its 500 on time;
  its code runs to the end.
- **Windows-only APIs throw `PlatformNotSupportedException`:** `System.Web.Mail.SmtpMail.Send`
  (`Util/SmtpMail.cs`), `Util.WorkItem.Post`, `Util.Transactions.InvokeTransacted` (page transactions),
  `HttpRequest.HttpChannelBinding`, and `IisTraceListener`/`IisTraceWebEventProvider`.
- **Build providers:** `.xsd` files in App_Code throw `NotImplementedException` when compiled
  (`Compilation/XsdBuildProvider.cs`); `App_WebReferences` produce no proxy (`Compilation/WebReferencesBuildProvider.cs`).
- **Windows authentication**, impersonation and `FileAuthorizationModule`'s ACL checks aren't available.
- `<system.data><DbProviderFactories>` entries from the app's configuration aren't registered; register factories in
  code with `DbProviderFactories.RegisterFactory`.
- `HttpUtility` follows .NET Framework 4.5+ (strict decoding); the 4.0 compatibility quirks of apps without
  `targetFramework` aren't emulated.
- A `.resx` file reference to an icon (`ResXFileRef` of type `Icon`) throws off Windows.
- `ApplicationHost.CreateApplicationHost` returns an object in a PseudoAppDomain without remoting; call it through
  `dynamic` or reflection.
- **Behaviour .NET changes everywhere:** `ArgumentException.Message` ends with `(Parameter 'name')`; ICU cultures
  (set `<PredefinedCulturesOnly>true` to keep .NET Framework's `CultureNotFoundException`); `\n` newlines; paths are
  case-sensitive and `\` isn't a separator.

### System.Web.Extensions (`src/WebFormsForCore.Web.Extensions`)

`System.Web.UI` (ScriptManager, UpdatePanel, Timer, extenders), `System.Web.UI.WebControls` (ListView, DataPager,
QueryExtender and others), `System.Web.Script.Serialization` (`JavaScriptSerializer`), `System.Web.Script.Services`
(script services, page methods), `System.Web.ApplicationServices`, `System.Web.ClientServices`,
`System.Web.Configuration`, `System.Web.Query.Dynamic`, and the MS AJAX client scripts.

Caveats: **`LinqDataSource` isn't available** (LINQ to SQL; `ui/WebControls/LinqDataSource.cs` is .NET Framework
only). `ApplicationServicesHostFactory` (WCF) isn't available. WCF service references are built with CoreWCF
primitives.

### System.Web.Services (`src/WebFormsForCore.Web.Services`)

`System.Web.Services`, `.Configuration`, `.Description`, `.Discovery`, `.Protocols`: ASMX web services and SOAP
client proxies. XML serialization uses .NET's `XmlSerializer`. The HTTP/MIME
WSDL importers are .NET Framework only; importing WSDL that uses HTTP bindings is untested.

### System.Configuration (`src/WebFormsForCore.Configuration`)

Assembly `System.Configuration.ConfigurationManager`, which replaces both the NuGet package and .NET Framework's
System.Configuration. Namespaces `System.Configuration` (`ConfigurationManager`, sections, providers, protected
configuration, CodeDom compiler configuration), `.Internal`, `.Provider`, and `System.Net.Configuration`.

The sources are Microsoft's Reference Source (System.Configuration.dll and the configuration parts of System.dll).

Caveats: `<system.net>` sections (`SmtpSection`, `DefaultProxySection`, ...) can be read with `ConfigurationManager.GetSection`, but .NET's System.Net doesn't apply them, so settings such as the default proxy or connection limits have to be set in code. `DataProtectionConfigurationProvider` uses webForCore's DPAPI emulation, so a section Windows DPAPI encrypted
can't be read on Linux (the error says so). `RsaProtectedConfigurationProvider` keeps key containers as XML files in
the key directory; containers exported with `aspnet_regiis -px` can be imported. The key directory is
`$WEBFORCORE_KEYS`, `/var/lib/webforcore/keys` or `~/.webforcore/keys`.

### Smaller companions

| Assembly | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.ApplicationServices | `System.Web.Security` (MembershipProvider, RoleProvider, ...), `System.Web.Configuration`, `System.Web.Hosting` | System.Web forwards the membership types here, as on .NET Framework |
| System.Web.DataVisualization | `System.Web.UI.DataVisualization.Charting` (Chart control) | Renders through the SkiaSharp System.Drawing |
| System.Web.Mobile | `System.Web.Mobile`, `System.Web.UI.MobileControls` (+ `.Adapters`) | Designers are .NET Framework only |
| System.Web.RegularExpressions | `System.Web.RegularExpressions` (`NetCoreRegexes`) | The classic public regex classes (`TagRegex`, `DirectiveRegex`, ...) are .NET Framework only; on .NET, System.Web's parser uses source-generated regexes. Code that uses the classic classes directly won't compile |
| Microsoft.Web.Infrastructure | `Microsoft.Web.Infrastructure`, `.DynamicModuleHelper`, `.DynamicValidationHelper` | webForCore's implementation of the documented API: `DynamicModuleUtility.RegisterModule` (on `HttpApplication.RegisterModule`), `HttpContextHelper`, `InfrastructureHelper`, `ValidationUtility` (granular request validation) |
| System.Web.Optimization, WebGrease | `System.Web.Optimization`, `Microsoft.AspNet.Web.Optimization.WebForms`, `WebGrease`, `Microsoft.Ajax.Utilities` | Bundling and minification |
| AjaxControlToolkit | `AjaxControlToolkit`, `.HtmlEditor`, `.Bundling` | Designers are .NET Framework only |
| System.Web.DynamicData, System.Web.Entity | | Not built by the solution |

## ASP.NET MVC, Web Pages, Razor (lifted)

| Assembly (version) | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.Mvc 5.3 | `System.Web.Mvc` (~250 types), `.Ajax`, `.Async`, `.Filters`, `.Html`, `.Razor`, `.Routing`, `.Routing.Constraints` | `LinqBinaryModelBinder` and the LINQ to SQL `Binary` editor support are compiled out; the EF 1 `EntityState` check too. Views are compiled at run time: turn off the ASP.NET Core Razor SDK in the project |
| Microsoft.Web.Mvc (MVC Futures) | `Microsoft.Web.Mvc`, `.Controls`, `.ModelBinding`, `.Resources` | `MvcSerializer`, `Html.Serialize` and `[Deserialize]` throw: NetDataContractSerializer doesn't exist on .NET |
| System.Web.Razor 3.0 | `System.Web.Razor`, `.Generator`, `.Parser`, `.Text`, `.Tokenizer`, `.Editor` | Generated code uses `\n` on Linux |
| System.Web.WebPages 3.0 | `System.Web.WebPages`, `.Html`, `.Scope`, `.Instrumentation`, `System.Web.Helpers` (AntiForgery) | Side-by-side Web Pages versions aren't supported |
| System.Web.WebPages.Razor 3.0 | `System.Web.WebPages.Razor`, `.Configuration` | |
| System.Web.WebPages.Deployment 3.0 | `System.Web.WebPages.Deployment` | Dependency checks use a collectible load context instead of an AppDomain |
| System.Web.Helpers 3.0 | `System.Web.Helpers` (WebGrid, Chart, WebImage, Crypto, Json) | Chart and WebImage render through the SkiaSharp System.Drawing |

## ASP.NET Web API (lifted)

| Assembly (version) | Namespaces | Caveats |
| --- | --- | --- |
| System.Web.Http 5.3 | `System.Web.Http` and `.Batch`, `.Controllers`, `.Dependencies`, `.Description`, `.Dispatcher`, `.ExceptionHandling`, `.Filters`, `.Hosting`, `.Metadata`, `.ModelBinding`, `.Results`, `.Routing`, `.Services`, `.Tracing`, `.Validation`, `.ValueProviders` | Model-state messages built from exceptions use .NET's `ArgumentException` format |
| System.Web.Http.WebHost 5.3 | `System.Web.Http.WebHost` | |
| System.Web.Http.Owin 5.3 | `System.Web.Http.Owin` | |
| System.Web.Http.Cors, System.Web.Cors 5.3 | `System.Web.Http.Cors`, `System.Web.Cors` | |
| System.Net.Http.Formatting 6.0 | `System.Net.Http`, `.Formatting`, `.Handlers`, `.Headers` | `ContentDispositionHeaderValue.FileName` is unquoted by .NET (Web API's providers handle this); indented JSON and XML use `\n` |

## OWIN / Katana (lifted)

Microsoft.Owin 4.2.3 and companions: `Microsoft.Owin` (+ `.Builder`, `.Extensions`, `.Infrastructure`, `.Logging`,
`.Mapping`, `.Security`), `Microsoft.Owin.Security` (+ `.Cookies`, `.OAuth`, `.DataHandler`, `.DataProtection`),
`Microsoft.Owin.Host.SystemWeb`, `Microsoft.Owin.Hosting`, `Microsoft.Owin.Host.HttpListener`,
`Microsoft.Owin.Testing`, `Microsoft.Owin.Cors`. `Owin.dll` (`IAppBuilder`) is the `Owin` 1.0 package.

Caveats: `DpapiDataProtectionProvider` uses the DPAPI emulation, so its payloads can't be exchanged with Windows. On
System.Web the machine key protects cookies, as on IIS. The "Domain" hosting starter throws (it needs AppDomains). The
HttpListener host uses Katana's Mono code paths. `HostString` accepts hosts that mix Unicode and punycode labels.

## SignalR 2 and ASP.NET Identity 2 (lifted)

| Assembly | Namespaces | Caveats |
| --- | --- | --- |
| Microsoft.AspNet.SignalR.Core 2.4.3 | `Microsoft.AspNet.SignalR` (+ `.Hubs`, `.Hosting`, `.Infrastructure`, `.Messaging`, `.Transports`, `.Json`, `.Owin`, `.Tracing`) | No scale-out backplanes (Redis, SQL Server, Service Bus) are lifted |
| Microsoft.AspNet.SignalR.SystemWeb 2.4.3 | `Microsoft.AspNet.SignalR`, `System.Web.Routing` | The obsolete `MapConnection`/`MapHubs` throw, as upstream; use `MapSignalR` |
| Microsoft.AspNet.SignalR.Client 2.4.3 | `Microsoft.AspNet.SignalR.Client` (+ `.Hubs`, `.Http`, `.Transports`) | The JavaScript client is the app's own script, unchanged |
| Microsoft.AspNet.Identity.Core / .EntityFramework / .Owin 2 | `Microsoft.AspNet.Identity`, `.EntityFramework`, `.Owin` | On Entity Framework 6.5; LocalDB is Windows only, so use SQL Server or another provider. Token providers use the DPAPI emulation |

## System.Drawing (lifted, new engine)

System.Drawing.Common 6.0 (Microsoft's managed code) over a GDI+ engine written on SkiaSharp. Namespaces
`System.Drawing`, `.Drawing2D`, `.Imaging`, `.Printing`, `.Text`, `.Design`. The `System.Drawing` assembly
(`src/WebFormsForCore.Drawing`) forwards .NET Framework's System.Drawing types to it.

Caveats:
- **Not implemented:** metafiles (EMF/WMF); multi-frame saving (`SaveAdd`); `GetEncoderParameterList`; graphics on
  an HDC, HWND or screen (`FromHdc`, `FromHwnd`, `CopyFromScreen`); most printing; `Font.FromHdc`;
  `GetHalftonePalette`; pen compound arrays; ICC profiles; JPEG-compressed TIFF.
- **Text:** no font fallback for missing glyphs, no right-to-left or vertical layout. Windows font names map to
  metric-compatible fonts (Liberation, Carlito, Caladea) through fontconfig.
- Output is close to Windows GDI+ but not pixel-identical.
- Microsoft's own suite: 3,716 of 3,883 pass on Linux, and 141 of the 151 failures are metafiles (see
  [TESTING.md](TESTING.md)).

## Other assemblies

| Assembly | Purpose |
| --- | --- |
| WebFormsForCore.Serialization.Formatters | A port of BinaryFormatter (and SoapFormatter) in the `WebFormsForCore.Serialization.Formatters.Binary` namespace, with surrogates for .NET Framework types .NET no longer serializes. System.Web uses it for out-of-process session state, view state and the output cache. Subclasses of `NameObjectCollectionBase` that override `GetObjectData` aren't handled |
| WebFormsForCore.Compilers | Roslyn `csc`/`vbc` for run-time compilation |
| aspnetcore_compiler | aspnet_compiler for .NET (precompiled sites) |
| webforcore-regiis | aspnet_regiis's protected-configuration commands |
