# IIS compatibility on Kestrel

On Windows, an ASP.NET 4.x app gets a lot from IIS before its code runs: request filtering, URL Rewrite, default
documents, custom errors, compression, logging, recycling and so on. webForCore rebuilds those parts in front of the
application, on Kestrel, and reads their settings from the `<system.webServer>` section of the app's own `web.config`.
An app keeps its IIS configuration as it is.

The reference behaviour is IIS 10 with ASP.NET 4.8. Every feature marked **Works** below has at least one test in the
IIS gate (`test/webForCore.IIS.Gate.Tests`, 58 tests) that drives `test/webForCore.IIS.Sample` over HTTP.

Status key: **Works**: implemented and tested. **Partial**: some of it, or by other means. **Not built**: could be done
and hasn't been. **Not possible**: Windows-native and can't exist on Linux.

## Turning it on

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddWebForms();            // compression, W3C logging
var app = builder.Build();
app.UseWebForms(o => o.HandleAllRequestsWithWebForms());   // IIS modules on by default
app.Run();
```

| Option | Default | Effect |
| --- | --- | --- |
| `UseWebForms(o => o.IIS(i => i.Enabled = false))` | on | Turns off the `<system.webServer>` modules |
| `UseWebForms(o => o.Recycling(r => ...))` | IIS's defaults | `Time` (1740 min), `Requests`, `PrivateMemory` (KB), `CheckInterval` (30 s): reaching one recycles the worker process (see [Process model](#process-model)) |
| `AddWebForms(o => o.Compression = false)` | on | IIS's static and dynamic compression |
| `AddWebForms(o => o.LogDirectory = ...)` | `logs/<site>/W3SVC1` next to the site folder | W3C log folder; never served, even if it is inside the site |

## How configuration is read

`WebServerConfiguration` (`src/WebFormsForCore.Web/WebFormsForCore/AspNetCoreHosting/IIS/`) builds the effective
`<system.webServer>` for each URL the way IIS does:
- It starts from IIS 10's applicationHost.config defaults, then applies the app's `web.config`, its `<location>`
  elements and the `web.config` files in subfolders.
- Attributes override inherited ones; collections follow `<add>`, `<remove>` and `<clear>`.
- Files are re-read when they change.

There's no `applicationHost.config`, so locking sections (`overrideMode`, `allowDefinition`) has no effect.

## Request pipeline modules

| IIS feature | Status | Behaviour on webForCore |
| --- | --- | --- |
| **Request filtering** (`<requestFiltering>`) | Works | IIS's default deny lists and your settings: file extensions 404.7 (`.cs`, `.config`, `.asax`, ...), hidden segments 404.8 (`bin`, `App_Code`, `App_Data`, ..., plus the build output folder and `obj`), verbs 404.6, double escaping and `+` 404.11, high-bit characters 404.12, `denyUrlSequences` 404.5, `denyQueryStringSequences` 404.18, `maxAllowedContentLength` 404.13, `maxUrl` 404.14, `maxQueryString` 404.15, `headerLimits` 404.10, `alwaysAllowedUrls` |
| **Concurrent request limit** (`serverRuntime appConcurrentRequestLimit`) | Works | 503.2 above the limit (5000) |
| **Custom headers** (`<httpProtocol>`) | Works | `customHeaders` on every response, including IIS's `X-Powered-By: ASP.NET`; `redirectHeaders` on 3xx; `removeServerHeader` |
| **HTTP redirection** (`<httpRedirect>`) | Works | Destination with the rest of the URL appended, `exactDestination`, `childOnly`, wildcards, status Permanent/Found/Temporary/PermRedirect |
| **URL Rewrite, inbound** (`<rewrite><rules>`) | Works | Rules at every level (web.config files and `<location>`s), applied as IIS distributes them: a level's rules apply below it, matched relative to that level, parents' rules first. `stopProcessing`, redirects, rewrites (a rewrite to `?query` alone is to the level's root), conditions, rewrite maps, `isFile`/`isDirectory`, the ECMAScript, Wildcard and ExactMatch syntaxes, `{CACHE_URL}` (`scheme://host:port/path?query`). Rules ASP.NET Core's parser can't read (some server variables, such as request headers other than Accept, Connection, Cookie, Forwarded, Host, Referer and User-Agent) fail closed: requests below their level get 500, and the error is logged |
| **URL Rewrite, outbound** (`<outboundRules>`) | Works | `RESPONSE_*` headers, and HTML bodies by `filterByTags` (A, Area, Base, Form, Frame, Head, IFrame, Img, Input, Link, Script, `<customTags>`) or the whole body. preConditions, conditions, `{R:n}`/`{C:n}`, rewrite maps, `{ToLower:}`/`{UrlEncode:}`/`{UrlDecode:}`. A compressed body isn't rewritten (IIS: 500.52) |
| URL Rewrite: rules that set server variables (`<serverVariables>`) | Not built | The rule's other actions apply; the variables aren't set |
| **IP and domain restrictions** (`<ipSecurity>`) | Works | First matching `<add>` decides (address with mask or prefix, or `domainName` with `enableReverseDns`), else `allowUnlisted`; `enableProxyMode` checks `X-Forwarded-For`; deny actions Forbidden (403.6, 403.8), Unauthorized, NotFound, AbortRequest |
| **Dynamic IP restrictions** (`<dynamicIpSecurity>`) | Works | `denyByConcurrentRequests` (403.501), `denyByRequestRate` (403.502), logging-only mode, proxy mode |
| **Default documents** (`<defaultDocument>`) | Works | IIS's list or yours. The document is found in any case, and the request is rewritten to its name as the list has it (`/` is `/default.aspx` with IIS's list), while `Request.RawUrl` stays the URL asked for (`/`), as on IIS (#127); 301 to the absolute URL with a missing trailing slash added, unless one of the application's routes takes the URL (`RouteExistingFiles`), as `UrlRoutingModule` does on IIS. The routes are known once the application has started (`Application_Start` runs with its first request) |
| **Directory browsing** (`<directoryBrowse>`) | Works | IIS's listing format and `showFlags`; hidden segments, the output folder and dot files are left out |
| Directory without a default document, browsing off | Partial | The request goes on to the application, as an extensionless URL does in integrated mode; IIS answers 403.14 |
| **Static content** (`<staticContent>`) | Works | `<mimeMap>` over IIS's default map, 404.3 for an unmapped file, `<clientCache>` (max-age, expires, no-cache, custom `Cache-Control`) |
| **HTTP errors** (`<httpErrors>`) | Works | `errorMode` (DetailedLocalOnly by default), `existingResponse` Auto/Replace/PassThrough, matching by status and substatus, File/ExecuteURL/Redirect. ASP.NET's own error pages set `TrySkipIisCustomErrors`, so Auto passes them through, as on IIS |
| **Managed modules** (`<modules>`) | Works | Entries with a managed type join `<system.web><httpModules>`; `<remove>`/`<clear>` act on the merged list, as integrated mode's single list does |
| **Managed handlers** (`<handlers>`) | Works | Managed entries are mapped before `<system.web><httpHandlers>`; `TransferRequestHandler` is skipped |
| **URL authorization** (`<security><authorization>`) | Works | Any matching deny denies, then allow, otherwise 401 |
| **Compression** (`<urlCompression>`, `<httpCompression>`) | Works | Through `AddWebForms()`: static on and dynamic off (IIS's defaults), MIME types, `minFileSizeForComp` (2,700 bytes), gzip |
| **W3C logging** | Works | Daily files with IIS's fields, in ASP.NET Core's field order; `cs-uri-query` keeps its `?`. No `sc-substatus`, `sc-win32-status` or `cs-username`. Outside the site by default, as IIS keeps them; a log folder inside the site answers 404.8. Entries are flushed every second; those not yet flushed when the process stops are lost |
| Output caching (`<caching>`, kernel cache) | Partial | ASP.NET's output cache works; IIS's kernel and user-mode caches aren't there |
| Server-side includes | Not built | |
| Failed request tracing, request monitor | Not built | Use ASP.NET Core logging and `dotnet-trace` |
| Configuration errors as 500.19 | Partial | System.Web reports `<system.web>` errors per request, as IIS does; a malformed `<system.webServer>` section is ignored |
| Event log, ETW providers, performance counters | Not possible | Use structured logging and .NET metrics |
| ISAPI, ISAPI filters, CGI, native modules | Not possible | Native Windows code |

## What the worker request gives ASP.NET

IIS hands ASP.NET a worker request (`HttpWorkerRequest`); on Kestrel this is `AspNetCoreWorkerRequest`.

| Feature | Status | Notes |
| --- | --- | --- |
| HTTP/1.1, keep-alive, chunked | Works | Kestrel |
| HTTP/2, HTTP/3 | Partial | Kestrel supports them; not tested with the fork |
| TLS, SNI, several bindings | Partial | Kestrel endpoints and certificates (`appsettings.json`); IIS bindings aren't read |
| HTTPS server variables, client certificates | Works | `IsSecureConnection`, `HTTPS`, `HTTPS_KEYSIZE`, `Request.ClientCertificate`, `CERT_*` variables in IIS's formats. The certificate is the one Kestrel negotiated (`ClientCertificateMode`); there's no renegotiation. `CERT_SERVER_*` and `HTTPS_SERVER_*` aren't available |
| Server variables | Works | IIS's set, including `INSTANCE_ID`, `APPL_MD_PATH`, `REMOTE_PORT`, `SERVER_SOFTWARE` (Kestrel); variables outside the classic list are asked of the worker request, as integrated mode does |
| WebSockets | Works | `HttpContext.AcceptWebSocketRequest` / `IsWebSocketRequest` as with IIS 8's module, `AspNetWebSocketContext`; Katana and SignalR use it. Open sockets aren't aborted when the app shuts down |
| `IsClientConnected` | Works | False once the client has gone or the request timed out |
| Bufferless input (`GetBufferlessInputStream`) | Works | |
| `executionTimeout` | Partial | .NET can't abort a thread. The client gets ASP.NET's 500 "Request timed out." on time, `Request.TimedOutToken` is cancelled, and the request ends at its next pipeline step; the page's code runs to the end, and its output is dropped |
| Integrated-pipeline APIs | Works | The fork runs the classic pipeline, but the APIs that need integrated mode work: `CurrentNotification`/`IsPostNotification`, `MapRequestHandler`/`LogRequest`/`PostLogRequest` events, writable `Request.Headers` and `ServerVariables`, `Response.Headers`, `AddOnSendingHeaders`, `InsertEntityBody`, `Request.Abort`, `Response.SubStatusCode`, `Server.TransferRequest` (a new request through the whole pipeline on the same connection; a chain deeper than 16 transfers fails, a guard against loops) |
| Response semantics | Works | Integrated mode's Content-Type rule, server name from the Host header, reason phrases, `X-AspNet-Version` and `Cache-Control: private` as integrated mode adds them (around a managed handler, on a flush and on an error; not on a response a module ends before the handler, as Katana's `OwinHttpModule` does) |
| Request URLs | Works | As IIS gives them to ASP.NET (#126): `Request.Path`, `FilePath`, `PathInfo` and `PATH_INFO` decoded (`/Product/Convertible Car`), with an escaped `/` and a `\` made `/` and repeated slashes merged, as http.sys does; `PathInfo` after the first segment with a managed handler's extension (`/Page.aspx/a.b/c`); `RawUrl` the URL asked for, decoded the same way, with the query string as sent, before any rewrite or default document (#127). `requestPathInvalidCharacters` gets ASP.NET's 400 and double escapes request filtering's 404.11. `HTTP_URL`, `UNENCODED_URL` and `CACHE_URL` are the integrated pipeline's and aren't set, as in IIS's classic pipeline |
| File names in URLs | Works | Case-insensitive on every platform, as on IIS's file system: a static file, page, handler or directory asked for in another case is found, the first time (#140). `Request.PhysicalPath` is the file's path as it is on disk |
| `Response.End`, `Redirect(url, true)`, `Server.Transfer`, `CompleteRequest` | Works | The handler stops and the pipeline goes on to `EndRequest` with the response still buffered, as after .NET Framework's thread abort, so `EndRequest`'s modules can change it: forms authentication turns a 401 ended with `Response.End()` (`WebSecurity.RequireAuthenticatedUser`) into its redirect to the log-in page (#129). Code that catches the `ThreadAbortException` and doesn't rethrow it runs on, as .NET can't rethrow it ([NAMESPACES.md](NAMESPACES.md)) |

## Process model

| IIS feature | Status | On webForCore |
| --- | --- | --- |
| App pool worker process | Works | `webforcore-host` (in the application's output, `bin_dotnet/webforcore-host/`) plays WAS's part: it owns the bindings and TLS and runs the application in worker processes, forwarding each request to the current one over a private Unix socket (#56). Run it with `dotnet bin_dotnet/webforcore-host/webforcore-host.dll`; starting the host itself (at boot, again if it ends) is the service manager's or container runtime's job, as it was Windows' for IIS. The client's address and port, the binding's, the scheme, the client certificate and the TLS strength reach `Request.ServerVariables` as on IIS; the Host header and the raw URL are the client's |
| AppDomain per application | Works | Each worker process runs one start of the application, so static state, `AppDomain.CurrentDomain` data (`DataDirectory`) and events are per start, as they were per AppDomain. The shared framework is the process's, as the GAC's assemblies were the AppDomain's |
| Restart on web.config, `bin` or `App_Code` change; `HttpRuntime.UnloadAppDomain` | Works | On every platform (`bin` and `App_Code` on Linux since #51). On Linux a worker holds a handful of inotify instances whatever the number of folders the site monitors (7 for a site monitoring files in 200 folders, #145, #152): System.Web watches the directories in the application's folder through three recursive watchers of that folder, one for each kind of change it watches for, and each change goes to the directories that asked for it, as their own watchers reported it; only a directory outside the folder (the compilation folder, the folder's parent) or below a symbolic link has a watcher of its own. ASP.NET Core's configuration reload and the `<system.webServer>` watcher take the others. Before 0.9.0 each monitored directory took an instance, and many two: #107's "12–15 per worker" held for small sites only, and Orchard 1.10, Kooboo CMS and nopCommerce needed 48 to more than 128. `<httpRuntime fcnMode>` applies (#146): `Single` watches the application's folder with one recursive watcher, and `Disabled` monitors nothing, so changes don't restart the application, as on IIS. `bin_dotnet` (the application's assemblies) counts as `bin`: a change in it restarts the application in `Default` and `Single` mode, and on Windows as on Linux the same changes do as on IIS 10 (CI's `iis-compare` job, #167). In `Single` mode, as on IIS, a change anywhere below `bin`/`bin_dotnet`, `App_Code` or another special folder restarts the application (a folder's LastWriteTime set included), and so does one in a folder whose name only starts with a special folder's (`binaries`), as IIS compares their names as prefixes; starting doesn't, though Windows reports a freshly copied site's folders as modified the first time the application opens them (their deferred LastWriteTime); IIS 10 restarts a freshly copied site for those at its first request in some runs. The limit, 128 per user by default (`fs.inotify.max_user_instances`), is shared by all of the user's processes, and a restart briefly holds two workers' instances: if several applications (or test runs) run as one user and one fails with "The configured user limit (128) on the number of inotify instances has been reached", raise it (`sysctl fs.inotify.max_user_instances=512`, and a file in `/etc/sysctl.d/` to keep it) or run them as different users. Each watched directory also takes an inotify watch (`fs.inotify.max_user_watches`, usually hundreds of thousands): a site with very many folders may need it raised. When monitoring can't start, the application doesn't start, the worker logs why, and each request shows "Failed to start monitoring changes" with the reason, as IIS showed a monitoring failure (#107). The worker tells the host, which starts the next worker; requests that arrive meanwhile wait for it, and none fails. The old worker finishes its requests and exits; those still running after `<hostingEnvironment shutdownTimeout>` (30 s) are aborted, as AppDomain.Unload did: long polls and WebSockets reconnect to the new worker. The workers share the compilation folder, as AppDomains did: the next one empties it when App_Code, `bin` or Web.config changed, but leaves the assemblies a running worker has loaded, marked `.delete` for a later start to remove, as .NET Framework could not delete them either, so the old worker still compiles pages against them (#89). Off Windows that is a lock each worker holds on them; it also makes an assembly a worker replaces itself (a page edited while it runs) count towards `numRecompilesBeforeAppRestart` (15), as on IIS. A restart costs a process start (and compiling what isn't precompiled). An application run on its own (`dotnet run`, without the host) stops on a restart, for whatever started it to start it again |
| Recycling by time, request count, private memory | Works | `Recycling(...)`, with IIS's defaults: a new worker |
| Freeing old domains | Works | The old worker process exits: everything it held goes with it |
| Compilation folder (Temporary ASP.NET Files) | Works | `<compilation tempDirectory>` when it is set, as on IIS. Otherwise `Temporary_ASP.NET_Files` in the temp folder (`Path.GetTempPath()`: `%TEMP%` on Windows, `$TMPDIR` or `/tmp` elsewhere), the folder .NET Framework fell back to when it couldn't write to its own (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\Temporary ASP.NET Files`), which .NET doesn't have. Off Windows `/tmp` is shared by every user, so the name ends with the user's name, `Temporary_ASP.NET_Files-www-data`, with characters other than ASCII letters, digits, `.`, `_` and `-` replaced by `_` (#57). Up to 0.7.0 it ended with the user's number (`-33`): the first start after the upgrade compiles the application again, and the old folder can be deleted |
| Health monitoring, rapid-fail protection | Works | The host replaces a worker that exits; after 5 failures in 5 minutes it starts no more and answers 503 (IIS stops the application pool). Startup and shutdown time limits (90 s) are IIS's. Settings: `WebForCore:Host` (`StartupTimeLimit`, `ShutdownTimeLimit`, `RapidFailProtection`, `RapidFailProtectionMaxCrashes`, `RapidFailProtectionInterval`, `QueueTimeout`, `RequestIdleTimeout`, `AcceptUntrustedClientCertificates`). |
| Request queue, 503 on overflow | Partial | `appConcurrentRequestLimit` (503.2); no HTTP.sys queue |
| Idle timeout, AlwaysRunning, preload | Not built | The app starts on its first request |
| Web gardens | Not built | One app folder per process: run several processes behind a load balancer, with out-of-process session state |
| Pool identity | Partial | Run each service as its own Linux user |
| CPU and memory limits | Partial | The service manager's or container runtime's (cgroups on Linux) |
| Several sites, applications, virtual directories in one process | Partial | One application per process, with an optional virtual path |

## Authentication

| IIS feature | Status | Notes |
| --- | --- | --- |
| Anonymous | Works | |
| Forms, ASP.NET Identity, OWIN cookies, OAuth | Works | All in managed code |
| URL authorization (`<system.web>` and `<system.webServer>`) | Works | |
| Windows (Negotiate, Kerberos, NTLM) | Not built | Kestrel's Negotiate handler exists but isn't connected to System.Web's `User` |
| Basic, Digest | Not built | There are no Windows accounts to check against |
| Impersonation | Not possible | There's no Windows identity to impersonate |
| WS-Federation through Windows Identity Foundation (`WSFederationAuthenticationModule`, `SessionAuthenticationModule`, `<system.identityModel>`) | Not available | .NET has no `System.IdentityModel` (WIF), #119. Katana's `Microsoft.Owin.Security.WsFederation` signs in with WS-Federation on OWIN ([NAMESPACES.md](NAMESPACES.md#summary-of-what-doesnt-work)) |

## Deployment tools

| IIS tool | webForCore |
| --- | --- |
| `aspnet_compiler` | `aspnetcore_compiler` (`src/WebFormsForCore.AspNetCompiler`), with aspnet_compiler's switches. See [Precompiled sites](#precompiled-sites) |
| `aspnet_regiis -pe/-pd/-pc/-px/-pi` | `webforcore-regiis` (`src/WebFormsForCore.RegIIS`). See [Protected configuration](#protected-configuration) |
| `aspnet_regsql` | `webforcore-regsql` (`src/WebFormsForCore.RegSql`, the .NET tool `webForCore.RegSql`), with aspnet_regsql's switches: `-A`/`-R` (membership, roles, profile, personalization, web events; `-Q`), `-ssadd`/`-ssremove` (`-sstype t`, `p` or `c`), `-sqlexportonly`, and `-ed`/`-dd`/`-et`/`-dt`/`-lt` (SQL cache dependency), on `-S`/`-U`/`-P`/`-E` or `-C`. The scripts are .NET Framework 4.8's, which System.Web carries for `SqlServices` too, and `-sqlexportonly` writes what aspnet_regsql writes, byte for byte (#123). Not the wizard (`-W`) |
| `appcmd`, IIS Manager | None. Edit `web.config`, and the service manager's configuration |

### Precompiled sites

`aspnetcore_compiler` writes the precompiled site into the folder `-b` names. A deployment uses the host's output folder
(`-b bin_dotnet`) and puts the host's build there too; the app then runs as usual (`dotnet bin_dotnet/<app>.dll` from
the site folder).

| Precompilation | What the target holds | On webForCore |
| --- | --- | --- |
| Fixed (no `-u`) | Marker files for the pages, `.compiled` maps, `App_Code.dll`, `App_global.asax.dll`, `App_GlobalResources.dll`, `App_Web_*.dll` | Pages are served from the precompiled assemblies. A page added later gets IIS's 500 "The file '...' has not been pre-compiled, and cannot be requested" |
| Updatable (`-u`) | The pages' markup with `CodeFile` replaced by `inherits="Class, App_Web_x"`; no code files | The markup is compiled at run time against the precompiled code-behind; edits and new pages take effect |

A compile error fails the precompilation with a non-zero exit code, reported as aspnet_compiler reports it
(`file(line): error CSxxxx: message`, with the physical path). Tested by `PrecompilationTests`.

### Protected configuration

`DataProtectionConfigurationProvider` (DPAPI) and `RsaProtectedConfigurationProvider` work on every platform. On
Windows they use Windows' DPAPI and key containers, as on .NET Framework. Elsewhere:

- **DPAPI is emulated** (`WebFormsForCore.Security.LocalDataProtection`): one master key per scope, as DPAPI keeps one
  per machine and one per user, in a file only its owner can read. Keys live in `$WEBFORCORE_KEYS` if set; otherwise
  machine-scope keys go in `/var/lib/webforcore/keys` when it is writable, and everything else in `~/.webforcore/keys`.
  A key is created the first time it's needed. Payloads are AES-256-CBC with an HMAC-SHA256, keyed by HKDF from the
  master key, a salt and the entropy; the wrong key or entropy fails with DPAPI's "Key not valid for use in specified
  state." Katana's `DpapiDataProtectionProvider` (ASP.NET Identity's token providers) uses the same emulation, in user
  scope.
- **A section encrypted by Windows DPAPI** is recognised by its blob header and fails with an explanation: those keys
  never leave the Windows machine. Decrypt it there (`aspnet_regiis -pdf`), then protect it again here
  (`webforcore-regiis -pef`) or with RSA and an exported key container.
- **RSA key containers are files**, `<container>.rsakey.xml` in the machine or user key directory, holding the key as
  XML. A bare `RSAKeyValue`, as `aspnet_regiis -px` exports it, is accepted, so a key exported from Windows can be
  imported. Keys are created on first use (2048-bit) unless they must already exist.
- **`webforcore-regiis`** has aspnet_regiis's configuration switches: `-pef`/`-pdf` (encrypt or decrypt a section of a
  site folder's web.config), `-pc` (create a container; `-exp` exportable, `-size`), `-pz` (delete one), `-px` (export;
  `-pri` with the private key), `-pi` (import), `-prov` and `-pku` (user scope). There's no IIS metabase, so `-pe`/`-pd`
  with `-app` aren't supported.

Tested by `ProtectedConfigurationTests`: RSA- and DPAPI-encrypted sections read by a page, the Windows-DPAPI
explanation, and a regiis export and import between two key directories.

## Compatibility decisions

Where .NET Framework, IIS and .NET disagree, these choices decide what webForCore does.

| Decision | Why |
| --- | --- |
| The reference is **.NET Framework 4.8 with `<httpRuntime targetFramework>` 4.5 or later**. The 4.0 compatibility quirks of apps without `targetFramework` (for example `HttpUtility`'s looser decoding) aren't emulated | The 4.5+ behaviour is the one Microsoft's reference source and tests describe, and any app can set `targetFramework` |
| **IIS 10**, in **classic pipeline mode**, with the integrated-only APIs provided on the Kestrel worker request ([above](#what-the-worker-request-gives-aspnet)) | Classic mode is how the reference source's `HttpRuntime` runs outside IIS; the integrated-only APIs are what apps call |
| **`HttpUtility` is Microsoft's** (in System.Web), not .NET's `System.Web.HttpUtility.dll` | It goes through `HttpEncoder.Current`, so `<httpRuntime encoderType>` applies and the encoding is .NET Framework's. The framework's copy is left out of the compiler's references (CS0433 otherwise) |
| **`System.Data.SqlClient`**, not Microsoft.Data.SqlClient, behind `SqlDataSource` and the SQL providers (membership, roles, profile, session, cache dependency) | The objects apps get and cast are the types they had on .NET Framework |
| **Files without a BOM are read in the ANSI code page** when `<globalization fileEncoding>` isn't set: Windows' system code page on Windows, and on Linux the one Windows has for the locale (`LANG`; windows-1252 for English and `LANG=C`), not .NET's UTF-8 `Encoding.Default` (#90, [MIGRATING.md](MIGRATING.md#6-code-changes)) | .NET Framework read them with `Encoding.Default`, the ANSI code page, so that is how files on an IIS server were saved. An application whose files are UTF-8 without a BOM sets `fileEncoding="utf-8"`, as it had to on IIS |
| **Session state is in process by default, in .NET Framework's `InProcSessionStateStore`**, as `<sessionState mode="InProc">` on IIS: values are the objects themselves, not serialized (#180). **Through `UseAspNetCoreSessionProvider()` it is out-of-process state in ASP.NET Core's distributed cache**, keyed by System.Web's session ID, not ASP.NET Core's session, and every value must be serializable, as with StateServer and SQLServer ([MIGRATING.md](MIGRATING.md#session-state)) | One session cookie, `<sessionState cookieName>` (`ASP.NET_SessionId`), and `<sessionState timeout>`, as on IIS; ASP.NET Core's session would add its own `.AspNetCore.Session` cookie and timeout (#79). The provider replaces StateServer (the ASP.NET State Service) and SQLServer, or shares sessions between servers; an application that was InProc on IIS doesn't need it |
| **Newlines are the platform's** in product output (`\n` on Linux): Razor's generated code, indented JSON and XML, exception messages. Only tests change | Rewriting product output would diverge from Microsoft's code. Tests compare newline-agnostically, or give Windows input where they hard-code positions |
| **BinaryFormatter output is .NET Framework's**: webForCore's .NET Framework 4.0 assemblies are written as `Version=4.0.0.0`, and .NET Framework types .NET no longer serializes have surrogates ([NAMESPACES.md](NAMESPACES.md#other-assemblies)) | Session state, view state and the output cache keep the format out-of-process stores and other servers expect |
| **Assemblies have the identity of the Microsoft assembly they replace** (name, version, culture, public key token), except that **System.Web is 11.0.0.0**, and System.Configuration.ConfigurationManager (.NET Framework's System.Configuration under .NET's name) 14.5.0.0 (#141) | Libraries built against Microsoft's assemblies only compile and bind against the same identity (BundleTransformer against System.Web.Optimization 1.1.0.0, CS0012). System.Web must outrank the shared framework's 4.0.0.0 facade of the same name, which the host would load otherwise, and System.Configuration.ConfigurationManager the package of that name that other packages bring; a higher version still binds a reference to Microsoft's. CI checks every packaged assembly (`tools/ci/check_assembly_identities.cs`) |

### Compared with IIS 10

CI's `iis-compare` job ([TESTING.md](TESTING.md#compared-with-iis-10), #78) sends the Web API gate's 66 requests
(`test/webForCore.WebApi.Sample`, through `GlobalConfiguration` and through OWIN under `/owin`) to the same sample built
for .NET Framework 4.8 with Microsoft's Web API 5.3.0 and Katana 4.2.3 packages, on IIS 10 (Windows Server 2025), and
compares the responses. In IIS's **integrated** pipeline every status, `Content-Type`, `Location`, `Allow`, body
(stack traces aside) and other header matches, including `Cache-Control: no-cache`, `Pragma` and `Expires: -1` on Web
API's own responses, `X-Powered-By`, `X-AspNet-Version` and `Cache-Control: private`, except these, which
`tools/ci/iis-known-differences.json` lists (the job fails on any other difference, and on a listed one that stops
occurring):

| Difference | Requests | Why |
| --- | --- | --- |
| `Server` is `Kestrel`, not `Microsoft-IIS/10.0` | all 66 | webForCore's host is Kestrel |
| An unmatched extensionless URL (no route matches) gets ASP.NET's 404 page, with `X-AspNet-Version`; IIS serves its native 404 page ("HTTP Error 404.0 - Not Found", `StaticFile` handler), without it. The status is 404 on both | 2 | In IIS's integrated pipeline a URL that no managed handler claims falls through to IIS's static file handler; webForCore gives every request to ASP.NET |

ASP.NET adds `X-AspNet-Version` and the default `Cache-Control: private` around a managed handler, when the response is
flushed and when it reports an error. The responses Katana's `OwinHttpModule` ends under `/owin`, before a handler
runs, have neither, on IIS and on webForCore, apart from the one that is flushed (`/api/status/text`: Web API doesn't
buffer content that knows its length, so each write flushes).

In IIS's **classic** pipeline, webForCore's reference ([above](#compatibility-decisions)), this sample doesn't run at
all. Katana's `OwinHttpModule` needs the integrated pipeline, so every request fails with a 500 ("This operation requires
IIS integrated pipeline mode"); and with the template's `Web.config`, `PUT`, `DELETE` and `OPTIONS` never reach ASP.NET
(IIS's 404.0), because the classic-mode extensionless handler accepts only `GET`, `HEAD`, `POST` and `DEBUG`. The job
records that run too, for reference. webForCore runs such an application as IIS's integrated pipeline does, apart from
the differences above, with the integrated-only APIs Katana uses provided on the Kestrel worker request.

#### Request handling

The job also compares request handling (#126, #127, #129, #140): `test/webForCore.Requests.Sample`, a Web Forms site of
pages, `App_Code` and static files that IIS serves as it is, gets the requests IIS.Gate.Tests' `RequestHandlingTests`
sent to it on Kestrel (escaped and invalid characters, path info, default documents, files in another case,
`Response.End`, `Redirect`, `Server.Transfer` and `CompleteRequest` before forms authentication's `EndRequest`). The
reference is IIS's **classic** pipeline, webForCore's; every status, `Location`, body and other header matches, except
these, which `tools/ci/iis-known-differences-requests.json` lists:

| Difference | Why |
| --- | --- |
| `Server` is `Kestrel`, not `Microsoft-IIS/10.0` | webForCore's host is Kestrel |
| A static file has `Cache-Control: public`, another `ETag`, `X-AspNet-Version`, and no `Accept-Ranges` or `Last-Modified` | ASP.NET's static file handler serves it (the worker request leaves out its `Last-Modified`, as Cassini, which it comes from, did); IIS's static file module serves it on IIS |
| A file that doesn't exist gets ASP.NET's 404 page, with `X-AspNet-Version`; IIS serves its native 404.0 page. Same status | As above |
| Request filtering's 404.11 is webForCore's short error page (`text/html`), without IIS's `Cache-Control: private` and `Connection: close`. Same status and substatus | IIS's detailed error pages aren't reproduced |
| ASP.NET's 400 page for a character of `requestPathInvalidCharacters` shows webForCore's source lines, stack frames and version information | The error and its message are the same; the details are the platform's |

The integrated pipeline is recorded too: it also sets `HTTP_URL`, `UNENCODED_URL` and `CACHE_URL` (`ServerVariables.aspx`).

How each part came to be is in git: the fork commits in `external/` and this repository's history
([MODIFICATIONS.md](MODIFICATIONS.md)).
