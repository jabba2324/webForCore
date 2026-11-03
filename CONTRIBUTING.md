# Contributing to webForCore

webForCore runs existing ASP.NET 4.x applications on .NET 10 (LTS) without rewriting them. A change is welcome when it makes
a real application behave more like it did on .NET Framework 4.8 and IIS 10, and when it can prove it with a test.

## The principles

1. **Only Microsoft assemblies.** webForCore contains and ships Microsoft's assemblies only: Microsoft's source lifted
   from its repositories, the port of the .NET Framework reference source, and assemblies with Microsoft's names that
   webForCore writes in their place (facades, `Microsoft.Web.Infrastructure`, `System.Net.Http.WebRequest`). Builds
   of third-party libraries for webForCore (WebActivatorEx, AjaxControlToolkit) are in
   [webForCore-thirdparty](https://github.com/jabba2324/webForCore-thirdparty), which exists only to help the
   [smoke test](https://github.com/jabba2324/webForCore-smoke-test) migrate applications that use them. Nothing in
   webForCore depends on it: not its code, packages, tests, CI or documentation. A problem a third-party library has on
   webForCore is fixed in webForCore's own code if it is webForCore's, and otherwise documented with a workaround.
   NuGet dependencies of Microsoft's libraries (Newtonsoft.Json, ...) and build-only tools are fine, and so is
   [drawingForCore](https://github.com/jabba2324/drawingForCore) (System.Drawing), which only ships Microsoft's code
   and its own GDI+ engine. A System.Drawing problem is fixed there; build webForCore against a checkout of it with
   `-p:DrawingForCoreSource=../drawingForCore` to try the fix here before it is released.

2. **Every change comes with a test.** A fix has a regression test that fails without it and passes with it; a feature
   has tests of its behaviour. Run the test against the code before your change and say in the pull request that it
   failed there. A documentation change that records a difference webForCore keeps (rather than fixing it) pins that
   behaviour with a test too, so the documentation can't silently become wrong. A new step in the migration guide is
   the one exception: its test is the application that needed it, so link that application's migration.

3. **Every change names a real application it affects.** Say which open-source (or otherwise describable) application
   hit the problem, what it did there, and what the user saw: "eShopLegacyMVC: every request failed with ...", not
   "ConfigurationManager could be wrong". Synthetic edge cases are fine as extra tests, but the reason for a change is an
   application that needs it. The [webForCore smoke test](https://github.com/jabba2324/webForCore-smoke-test) migrates
   open-source applications and files what breaks; its issues and `FINDINGS.md` are a good source.

4. **Avoid changing Microsoft's code at all costs.** Most of webForCore is Microsoft's own source, lifted unchanged
   (ASP.NET Web Stack, Katana, SignalR, Identity) or ported from the .NET Framework reference source
   (System.Web, System.Configuration). Its value is that it *is* Microsoft's behaviour. Before touching those files,
   look for a fix in webForCore's own code: the hosting layer (`src/WebFormsForCore.Web/WebFormsForCore/AspNetCoreHosting`,
   the IIS modules), the host (`src/WebFormsForCore.Host`), the build targets (`src/webForCore.WebStack.targets`), the lift scripts'
   additions (`tools/lift/additions/`), or configuration. Most problems found so far were fixed there.

5. **When Microsoft's code must change, keep the documentation current.** Keep the change as small as it can be (an
   `#if`, a guarded branch for Linux, not a rewrite) and:
   - mark it with a `// webForCore:` comment saying why, and the issue number;
   - for lifted code, make the change a fixup in the lift script (`tools/lift/`), not an edit of the lifted file, and
     run the lift again;
   - add the reason to [`LIFT.md`](LIFT.md), the engineering log, and the kind of change to
     [`docs/MODIFICATIONS.md`](docs/MODIFICATIONS.md);
   - regenerate [`docs/lifted-changes.md`](docs/lifted-changes.md) (`tools/lift/changes_report.py`), which lists every
     file that differs from upstream with its `webForCore:` notes.

## How webForCore's own code is written

These rules are for the code webForCore writes itself: the hosting layer and the host, the IIS modules, shims and
facades, build tasks and tools. They don't apply to Microsoft's code that is lifted or ported unchanged (principle 4
keeps that as it is). The aim is a runtime that is credible and stable: code a reviewer can see is correct, that keeps
working when .NET is patched, and that fails safely.

6. **No reflection into .NET's internals.** Don't read, write or call non-public members of the runtime, the shared
   framework, ASP.NET Core or any other library, and don't depend on their private names or layout. They change in
   any release, patch releases included, without notice, and code that edits another component's private state can't
   take that component's locks. Use public APIs. If a behaviour needs something .NET doesn't expose, document the
   difference (with a test) instead. Reflection over the *application's* own types, to do what ASP.NET did on .NET
   Framework (create the type a `Web.config` names, find `Global.asax`'s class), is fine. Use public members where
   there are any, and say in a comment why reflection is needed.

7. **No unsafe code and no GC tricks.** No `unsafe` blocks or pointers, no `Unsafe`/`MemoryMarshal` reinterpretation,
   no P/Invoke. And nothing that steers the garbage collector: no `GC.Collect`, `GC.WaitForPendingFinalizers` or
   `GC.AddMemoryPressure`, no registries of `GCHandle`s, no polling weak references to find out whether something has
   been collected. If a design needs the GC to free something at a particular moment, change the design: release it
   with `IDisposable`, or put a process boundary around it, as application restarts do (#56).

8. **Slow, secure and stable beats fast.** Don't optimise for performance before a real application shows a problem
   with a measurement. Choose the simplest design that is clearly correct and safe over a faster one that is harder
   to reason about. For example, the host forwards each request to the worker process over HTTP instead of handing
   sockets between processes. When performance does need work, the pull request includes the measurement.

9. **Programs and tools are .NET.** Everything this repository builds, and every tool it uses (CI helpers, generators,
   checks), is written in C# as a project or as a file-based app run with `dotnet run tool.cs`. No Python, Node.js,
   Ruby, Perl or other general-purpose language. Shell scripts (bash, PowerShell) and CI YAML are fine as glue that
   calls dotnet, git, gh and curl. Anything that parses data or makes decisions belongs in C#. Client-side JavaScript
   and CSS that the libraries serve to browsers are part of the product, not tooling, so this rule doesn't cover
   them.

Code written before rules 6–8 that breaks them is listed in #57, and Python written before rule 9 in #63. Each item is
removed or replaced when it is next changed. New code doesn't add to either list.

## Where tests go

| What changed | Test |
| --- | --- |
| Behaviour of a running application (System.Web, the hosting layer, IIS modules, configuration) | A page in `test/webForCore.IIS.Sample` (problems found in applications go in `regressions/`) and a test in `test/webForCore.IIS.Gate.Tests` (`RegressionTests` for those), or the MVC, Web API and Web Pages gate suites |
| How an application builds or what the packages contain | `tools/ci/package-smoke` (Web Forms) or `tools/ci/package-smoke-mvc` (MVC, OWIN and third-party packages): applications outside this repository's build files that use only the packages |
| A lifted library | Its upstream test suite, lifted with it; add webForCore's test next to it if upstream has none |

The suites and how to run them are in [`docs/TESTING.md`](docs/TESTING.md). Run at least the suites your change can
affect, and the whole set (`tools/ci/run-tests.sh`) before a release.

## Documentation

webForCore's documentation says what works and what doesn't; keep it true when behaviour changes:
[`docs/NAMESPACES.md`](docs/NAMESPACES.md) (what each assembly provides and its caveats),
[`docs/IIS-COMPATIBILITY.md`](docs/IIS-COMPATIBILITY.md) (the IIS features), [`docs/MIGRATING.md`](docs/MIGRATING.md)
and [`docs/AGENT-MIGRATION.md`](docs/AGENT-MIGRATION.md) (the steps an application takes). A difference from IIS that
webForCore keeps belongs in one of them, with the reason and what an application does instead.

## Pull requests

- One problem per commit, with the application that hit it and `Fixes #<issue>` in the message.
- The pull request lists, for each change: the problem, the application, the fix, the test, and whether Microsoft's code
  changed (and where that is documented).
- CI (`.github/workflows/ci.yml`) builds, runs every suite, packs the packages and runs the package smoke test, on Linux
  and on Windows (a development environment, #46); both must pass.

Licences: webForCore's own code is MIT; lifted code keeps its licence ([`docs/LICENSING.md`](docs/LICENSING.md)).
Contributions are accepted under the licence of the file they change.
