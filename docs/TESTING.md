# Tests

webForCore is tested mainly with **other people's tests**: the suites Microsoft and Mono wrote for the libraries
webForCore runs. They are lifted with the code ([MODIFICATIONS.md](MODIFICATIONS.md)) and run unchanged, except
where .NET's base class library behaves differently from .NET Framework's; those corrections are listed in
[lifted-changes.md](lifted-changes.md). On top of them, webForCore's own **gate suites** run real sample applications
on Kestrel and check them over HTTP against IIS's documented behaviour.

**Latest run (round 19, 31 Oct 2026, Linux x64, Release): 27,297 tests in 24 suites. 27,111 pass, 35 are skipped
with a reason, and 151 fail, all of them known System.Drawing differences (141 are metafiles).** In the full run one
more test failed, a race between three of Microsoft's MVC Futures test classes over a static setting; they now share an
xUnit collection, and Microsoft.Web.Mvc.Test passed in three runs since.

## Running the tests

```bash
tools/ci/fetch-test-fonts.sh     # System.Drawing's test fonts (not redistributable, so not committed); before the build
dotnet build src/WebFormsForCore.Build/WebFormsForCore.Build.csproj -c Release
dotnet build WebFormsForCore.slnx -c Release
tools/lift/sqlserver.sh          # SQL Server in a container for Identity.Test (podman or docker)
tools/ci/run-tests.sh            # every suite; .trx files and summary.md in ./TestResults
```

One suite: `dotnet test test/webForCore.<Suite>/webForCore.<Suite>.csproj -c Release`.

CI (`.github/workflows/ci.yml`) runs the same script on Ubuntu 24.04 for every push and pull request, with SQL Server
as a service container. A suite fails CI on any failing test, except System.Drawing.Common.Tests: there, CI fails only
on a failure that isn't in `tools/ci/known-failures/System.Drawing.Common.Tests.txt`, and it reports known failures
that now pass, so the list only shrinks.

The default branch, `webforcore-lift`, is protected: a pull request can merge only once CI's `build-and-test` job has
passed on it (packing and the package smoke test included). Force pushes and deleting the branch are blocked.
Repository admins can still push to the branch directly. GitHub runs CI on a first-time contributor's pull request
only after a maintainer approves the run.

After the tests, CI packs the NuGet packages (version `<Version>-ci.<run number>`; a `v<Version>` tag builds the
release version and attaches the packages to a GitHub release, with `docs/release-notes/<version>.md` as its notes) and runs `tools/ci/package-smoke.sh`: a Web Forms app
that references only the `webForCore` package from that folder is built, started, and must serve a page and code
compiled from App_Code, and with `relaxedUrlToFileSystemMapping` on, map a path (`MapPath.aspx`). Then `tools/ci/package-smoke-mvc`, an MVC 5 and OWIN app with the third-party packages real
applications have (Unity.Mvc, WebActivatorEx, Katana's Google provider) as they have them, must render Razor views with webForCore's MVC, Razor, Web Pages and Helpers in its `deps.json`,
construct a controller through Unity (started from `Application_Start`, since WebActivatorEx's start methods don't
run on webForCore), and redirect a Google challenge.

## The suites

### Lifted from Microsoft and Mono

| Suite | Upstream | Tests | Passed | Skipped | What it exercises |
| --- | --- | ---: | ---: | ---: | --- |
| System.Web.MonoTests | Mono `mcs/class/System.Web/Test` | 2,634 | 2,628 | 6 | System.Web itself: HttpRequest/Response, caching, configuration, controls, forms authentication, hosting, HttpUtility. Corrected to Microsoft's 4.5+ behaviour where Mono's implementation differed |
| System.Web.Mvc.Test | AspNetWebStack | 5,498 | 5,498 | 0 | MVC 5 |
| Microsoft.Web.Mvc.Test | AspNetWebStack | 456 | 456 | 0 | MVC Futures |
| System.Web.Razor.Test | AspNetWebStack | 1,460 | 1,460 | 0 | Razor 3 parser and code generator |
| System.Web.WebPages.Test | AspNetWebStack | 973 | 971 | 2 | Web Pages |
| System.Web.WebPages.Razor.Test | AspNetWebStack | 46 | 45 | 1 | Web Pages' Razor host |
| System.Web.WebPages.Deployment.Test | AspNetWebStack | 52 | 48 | 4 | Web Pages deployment and version checks |
| System.Web.Helpers.Test | AspNetWebStack | 418 | 418 | 0 | WebGrid, Chart, WebImage, Crypto, JSON (on the SkiaSharp System.Drawing) |
| System.Web.Http.Test | AspNetWebStack | 3,157 | 3,157 | 0 | Web API 2 |
| System.Web.Http.WebHost.Test | AspNetWebStack | 343 | 343 | 0 | Web API on System.Web |
| System.Web.Http.Owin.Test | AspNetWebStack | 243 | 243 | 0 | Web API on OWIN |
| System.Web.Http.Cors.Test, System.Web.Cors.Test | AspNetWebStack | 196 | 196 | 0 | CORS |
| System.Net.Http.Formatting.Test | AspNetWebStack | 6,445 | 6,445 | 0 | Web API's formatters, multipart, HttpClient extensions |
| Microsoft.Owin.Tests | Katana | 321 | 321 | 0 | OWIN core |
| Microsoft.Owin.Host.SystemWeb.Tests | Katana | 36 | 36 | 0 | OWIN on System.Web |
| Microsoft.Owin.Security.Tests | Katana | 118 | 114 | 4 | Cookie, OAuth and other authentication middleware |
| Microsoft.AspNet.SignalR.Tests | SignalR 2.4.3 | 489 | 487 | 2 | SignalR server and client |
| Identity.Test | ASP.NET Identity 2 | 311 | 311 | 0 | Identity on Entity Framework 6 against SQL Server |
| System.Drawing.Common.Tests | dotnet/runtime 6.0 | 3,883 | 3,716 | 16 | System.Drawing on the SkiaSharp GDI+ engine (Microsoft's Linux selection); 151 known failures |

Every skip is the upstream project's own (`Skip=` in its source, or a category Microsoft excludes on Linux). None was
added to hide a failure. The System.Drawing failures are 141 metafile tests (EMF/WMF aren't implemented), 8
deliberate differences from libgdiplus or .NET Core, 1 comparison against Windows' pixels and 1 premultiplied-alpha
rounding difference; they are listed in `tools/lift/test-results/2026-10-30.md`.

### webForCore's own

| Suite | Tests | What it checks |
| --- | ---: | --- |
| IIS.Gate.Tests | 97 | `test/webForCore.IIS.Sample` on Kestrel: `RegressionTests` (21) for problems real applications hit (`regressions/`, one test per issue, each naming the application); each `<system.webServer>` feature against IIS 10's documented behaviour (request filtering, rewrite rules, redirects, default documents, errors, IP restrictions, directory browsing, compression, logging), the in-process restart, WebSockets, HTTPS and client certificates, timeouts, SignalR, bundling, protected configuration, integrated-pipeline APIs, precompiled sites (`aspnetcore_compiler`: fixed, updatable, compile errors, usage), ASMX (SOAP 1.1/1.2, faults, HTTP POST, WSDL, help page, JSON), ASP.NET AJAX (ScriptResource.axd, UpdatePanel partial postback, page methods), `System.Web.Mail`, `<system.net>` (mail settings through a PasswordRecovery postback to a fake SMTP server, the default proxy through a fake proxy), `<DbProviderFactories>`, data binding over app types, a Visual Studio typed DataSet with TableAdapters against SQL Server, and AppDomain restarts: the old domain is collected, and a WebSocket outlasting `shutdownTimeout` is aborted |
| WebApi.Gate.Tests | 67 | `test/webForCore.WebApi.Sample` (Visual Studio's Web API template and the "Getting Started" tutorial's controllers). Each request runs through System.Web and through OWIN, and is compared with an oracle: the same controllers in Web API's in-memory server, without System.Web. Status, media type, body, Location and Allow must match. Also: a restart, after which the old domain must be collected |
| Mvc.Gate.Tests | 65 | `test/webForCore.Mvc.Sample` and `test/webForCore.WebPages.Sample`: model binding, validation, post-redirect-get with TempData, `Html.Serialize`/`[Deserialize]` state (and tampering), anti-forgery tokens, filters, error pages, layouts, display and editor templates, Razor features, MVC Futures, Web Pages, that source files aren't served, and that the old domain is collected after a restart |
| Serialization.Tests | 10 | `NameValueCollection` and its subclasses through the BinaryFormatter port, in .NET Framework's format |

## Code coverage

Measured over all 24 suites with `dotnet-coverage` (`tools/ci/coverage.sh`, round 13), including the sample apps
the gate suites start in their own processes. Line coverage of the production assemblies (tests, samples and NuGet
dependencies excluded):

| | Coverable lines | Covered | Line coverage |
| --- | ---: | ---: | ---: |
| Lifted Microsoft code (Web Stack, Katana, SignalR, Identity, System.Drawing.Common) | 77,188 | 60,422 | **78.3%** |
| WebFormsForCore's port (System.Web and companions) and webForCore's tools | 301,611 | 59,361 | **19.7%** |
| **All** | **378,799** | **119,783** | **31.6%** |

The lifted libraries come with Microsoft's own suites, so their coverage is close to what Microsoft had: MVC 91%,
Web API 91%, Razor 90%, Formatting 90%. The port is the weak spot:
- **System.Web (27.8% of 146,216 lines)** is covered by Mono's tests and the gates. The hosting layer webForCore
  added is well covered (the IIS layer 84%, the worker request 69%), but large parts of the Web Forms control set
  and the configuration sections are only exercised indirectly.
- **System.Web.Services (ASMX) had no coverage at all**, and **System.Web.Extensions** (AJAX, ScriptManager,
  UpdatePanel) had 3.1%. Since these figures, the IIS gate has suites for both (`WebServiceTests`, `AjaxTests`), but
  they cover the main paths only; both remain thin.
- System.Web.DataVisualization (8.9%) and WebGrease (7.5%) are large and only touched through the Chart helper and
  bundling.

Per assembly:

| Assembly | Source | Coverable lines | Covered | Line coverage |
| --- | --- | ---: | ---: | ---: |
| Microsoft.AspNet.Identity.Core | lifted | 1,302 | 1,258 | 96.6% |
| Microsoft.AspNet.Identity.EntityFramework | lifted | 517 | 442 | 85.5% |
| Microsoft.AspNet.Identity.Owin | lifted | 424 | 225 | 53.1% |
| Microsoft.AspNet.SignalR.Client | lifted | 3,105 | 1,773 | 57.1% |
| Microsoft.AspNet.SignalR.Core | lifted | 6,172 | 4,624 | 74.9% |
| Microsoft.AspNet.SignalR.SystemWeb | lifted | 13 | 0 | 0.0% |
| Microsoft.Owin | lifted | 1,759 | 1,219 | 69.3% |
| Microsoft.Owin.Cors | lifted | 74 | 0 | 0.0% |
| Microsoft.Owin.Host.HttpListener | lifted | 1,812 | 492 | 27.2% |
| Microsoft.Owin.Host.SystemWeb | lifted | 2,847 | 1,578 | 55.4% |
| Microsoft.Owin.Hosting | lifted | 853 | 466 | 54.6% |
| Microsoft.Owin.Security | lifted | 689 | 436 | 63.3% |
| Microsoft.Owin.Security.Cookies | lifted | 328 | 253 | 77.1% |
| Microsoft.Owin.Security.OAuth | lifted | 871 | 790 | 90.7% |
| Microsoft.Web.Mvc | lifted | 2,964 | 1,654 | 55.8% |
| System.Drawing.Common | lifted | 13,020 | 9,763 | 75.0% |
| System.Net.Http.Formatting | lifted | 4,141 | 3,724 | 89.9% |
| System.Web.Cors | lifted | 216 | 216 | 100.0% |
| System.Web.Helpers | lifted | 2,146 | 1,696 | 79.0% |
| System.Web.Http | lifted | 9,552 | 8,657 | 90.6% |
| System.Web.Http.Cors | lifted | 372 | 362 | 97.3% |
| System.Web.Http.Owin | lifted | 755 | 548 | 72.6% |
| System.Web.Http.WebHost | lifted | 1,172 | 877 | 74.8% |
| System.Web.Mvc | lifted | 11,118 | 10,133 | 91.1% |
| System.Web.Razor | lifted | 6,368 | 5,704 | 89.6% |
| System.Web.WebPages | lifted | 3,921 | 3,020 | 77.0% |
| System.Web.WebPages.Deployment | lifted | 320 | 200 | 62.5% |
| System.Web.WebPages.Razor | lifted | 357 | 312 | 87.4% |
| aspnetcore_compiler | port | 476 | 168 | 35.3% |
| WebFormsForCore.Compilers | port | 51 | 40 | 78.4% |
| System.Configuration.ConfigurationManager | port | 14,125 | 5,530 | 39.2% |
| System.Drawing | port | 215 | 0 | 0.0% |
| webforcore-regiis | port | 54 | 44 | 81.5% |
| WebFormsForCore.Serialization.Formatters | port | 6,111 | 2,944 | 48.2% |
| System.Web | port | 146,216 | 40,667 | 27.8% |
| System.Web.ApplicationServices | port | 389 | 90 | 23.1% |
| System.Web.DataVisualization | port | 51,639 | 4,603 | 8.9% |
| System.Web.Extensions | port | 16,527 | 511 | 3.1% |
| Microsoft.Web.Infrastructure | port | 551 | 63 | 11.4% |
| System.Web.Optimization | port | 1,255 | 634 | 50.5% |
| System.Web.RegularExpressions | port | 3,407 | 946 | 27.8% |
| System.Web.Services | port | 18,737 | 0 | 0.0% |
| WebGrease | port | 41,858 | 3,121 | 7.5% |
| **Total** | | **378,799** | **119,783** | **31.6%** |

Branch coverage isn't reported: dotnet-coverage's Cobertura output has no branch data. To measure again:
`dotnet tool install -g dotnet-coverage`, build in Release, then `tools/ci/coverage.sh` (about 10 minutes).

## How results are recorded

- `tools/lift/test-results/2026-10-30.md`: results of each round, including failures grouped by message.
- `LIFT.md`: why each test was corrected or skipped.
- `tools/lift/trx_summary.py` and `trx_failures.py` summarise `.trx` files; `tools/ci/check_failures.py` compares a
  run with the known-failures list.
