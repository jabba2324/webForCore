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
builder.Services.AddWebForms();            // compression, W3C logging, systemd integration
var app = builder.Build();
app.UseWebForms(o => o.HandleAllRequestsWithWebForms());   // IIS modules on by default
app.Run();
```

| Option | Default | Effect |
| --- | --- | --- |
| `UseWebForms(o => o.IIS(i => i.Enabled = false))` | on | Turns off the `<system.webServer>` modules |
| `UseWebForms(o => o.IsolateApplication(false))` | isolated | Runs the app in the default load context, so there's no in-process restart (see [Process model](#process-model)) |
| `UseWebForms(o => o.Recycling(r => ...))` | IIS's defaults | `Time` (1740 min), `Requests`, `PrivateMemory` (KB), `MaxStuckDomains` (3), `CheckInterval` (30 s) |
| `AddWebForms(o => o.Compression = false)` | on | IIS's static and dynamic compression |
| `AddWebForms(o => o.LogDirectory = ...)` | `../logs/W3SVC1` | W3C log folder |
| `AddWebForms(o => o.Systemd = false)` | on | `sd_notify` readiness and a watchdog |

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
| **Default documents** (`<defaultDocument>`) | Works | IIS's list or yours, case-insensitive; 301 to add a missing trailing slash, unless one of the application's routes takes the URL (`RouteExistingFiles`), as `UrlRoutingModule` does on IIS. The routes are known once the application has started (`Application_Start` runs with its first request) |
| **Directory browsing** (`<directoryBrowse>`) | Works | IIS's listing format and `showFlags`; hidden segments, the output folder and dot files are left out |
| Directory without a default document, browsing off | Partial | The request goes on to the application, as an extensionless URL does in integrated mode; IIS answers 403.14 |
| **Static content** (`<staticContent>`) | Works | `<mimeMap>` over IIS's default map, 404.3 for an unmapped file, `<clientCache>` (max-age, expires, no-cache, custom `Cache-Control`) |
| **HTTP errors** (`<httpErrors>`) | Works | `errorMode` (DetailedLocalOnly by default), `existingResponse` Auto/Replace/PassThrough, matching by status and substatus, File/ExecuteURL/Redirect. ASP.NET's own error pages set `TrySkipIisCustomErrors`, so Auto passes them through, as on IIS |
| **Managed modules** (`<modules>`) | Works | Entries with a managed type join `<system.web><httpModules>`; `<remove>`/`<clear>` act on the merged list, as integrated mode's single list does |
| **Managed handlers** (`<handlers>`) | Works | Managed entries are mapped before `<system.web><httpHandlers>`; `TransferRequestHandler` is skipped |
| **URL authorization** (`<security><authorization>`) | Works | Any matching deny denies, then allow, otherwise 401 |
| **Compression** (`<urlCompression>`, `<httpCompression>`) | Works | Through `AddWebForms()`: static on and dynamic off (IIS's defaults), MIME types, `minFileSizeForComp` (2,700 bytes), gzip |
| **W3C logging** | Works | Daily files with IIS's fields, in ASP.NET Core's field order; `cs-uri-query` keeps its `?`. No `sc-substatus`, `sc-win32-status` or `cs-username` |
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
| Integrated-pipeline APIs | Works | The fork runs the classic pipeline, but the APIs that need integrated mode work: `CurrentNotification`/`IsPostNotification`, `MapRequestHandler`/`LogRequest`/`PostLogRequest` events, writable `Request.Headers` and `ServerVariables`, `Response.Headers`, `AddOnSendingHeaders`, `InsertEntityBody`, `Request.Abort`, `Response.SubStatusCode`, `Server.TransferRequest` |
| Response semantics | Works | Integrated mode's Content-Type rule, server name from the Host header, reason phrases, `X-AspNet-Version`, `Cache-Control: private` |

## Process model

| IIS feature | Status | On webForCore |
| --- | --- | --- |
| App pool worker process | Partial | One Kestrel process per app, supervised by systemd (`tools/deploy/webforcore@.service`) or a container runtime |
| AppDomain per application | Works | `PseudoAppDomain`: the app runs in a collectible `AssemblyLoadContext`, with its own copy of System.Web and the app's assemblies, so static state is per domain |
| Restart on web.config, `bin` or `App_Code` change; `HttpRuntime.UnloadAppDomain` | Works | A new domain starts while the old one drains (overlapped recycle); no requests fail. Requests still running in the old domain after `<hostingEnvironment shutdownTimeout>` (30 s) are aborted, as AppDomain.Unload did: long polls and WebSockets reconnect to the new domain |
| Recycling by time, request count, private memory | Works | `Recycling(...)`, with IIS's defaults |
| Freeing old domains | Works | An old domain is collected once its requests finish (gate tests restart the IIS, MVC and Web API samples and wait for it). What .NET keeps process-wide is released when it unloads; an app's own `MemoryCache` instances must be disposed in `Application_End`. A domain still referenced 5 minutes after shutdown is logged, and after 3 the process stops cleanly for systemd to restart it, as WAS recycled a worker |
| Health monitoring, rapid-fail protection | Works | systemd unit: `Type=notify`, watchdog in place of WAS's ping, 5 failures in 5 minutes |
| Request queue, 503 on overflow | Partial | `appConcurrentRequestLimit` (503.2); no HTTP.sys queue |
| Idle timeout, AlwaysRunning, preload | Not built | The app starts on its first request |
| Web gardens | Not built | One app folder per process: run several processes behind a load balancer, with out-of-process session state |
| Pool identity | Partial | Run each service as its own Linux user |
| CPU and memory limits | Partial | cgroups through systemd or the container runtime |
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

## Deployment tools

| IIS tool | webForCore |
| --- | --- |
| `aspnet_compiler` | `aspnetcore_compiler` (`src/WebFormsForCore.AspNetCompiler`): fixed and updatable (`-u`) precompilation with aspnet_compiler's switches; use `-b bin_dotnet` |
| `aspnet_regiis -pe/-pd/-pc/-px/-pi` | `webforcore-regiis` (`src/WebFormsForCore.RegIIS`): `-pef`/`-pdf` on a site folder, RSA key containers as files, DPAPI emulated with a master key file |
| `appcmd`, IIS Manager | None. Edit `web.config` and the systemd unit |

Implementation history and the reasons behind each choice are in `LIFT.md`, under "AppDomains and IIS on Kestrel",
"The worker request" and "Protected configuration, the integrated pipeline and precompiled sites".
