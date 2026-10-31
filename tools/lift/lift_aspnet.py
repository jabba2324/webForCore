#!/usr/bin/env python3
"""Lift ASP.NET SignalR 2 (SignalR/SignalR, tag 2.4.3, Apache License 2.0) and ASP.NET Identity 2 (aspnet/AspNetIdentity,
MIT) into the fork as SDK-style projects for net11.0.

SignalR's projects are SDK-style (KoreBuild): their items are the project folder's default globs plus the explicit
<Compile Include Link> items. Identity's are old-style: their explicit <Compile> and <EmbeddedResource> items. Files are
copied byte for byte to src/webForCore.<Name>/ (tests to test/) at their path below the project (or its Link), then the
FIXUPS below are applied. References map to the fork's lifted Katana and System.Web, and to nuget.org packages at the
versions the rest of the fork uses. Assemblies keep their names and are delay signed with Microsoft's public key, as
Microsoft's builds are (PublicKeyToken 31bf3856ad364e35).

Usage: lift_aspnet.py <SignalRRoot> <AspNetIdentityRoot> <ForkRoot> [Name ...]
Writes tools/lift/manifests/<Name>.tsv.
"""
import glob, os, re, shutil, subprocess, sys
import xml.etree.ElementTree as ET

NEWTONSOFT = ("Newtonsoft.Json", "13.0.4")
TEST_PACKAGES = [("Microsoft.NET.Test.Sdk", "18.11.0"), ("xunit", "2.9.3"), ("xunit.runner.visualstudio", "3.1.5"),
                 ("Moq", "4.21.0"), ("Castle.Core", "5.2.1")]
FORK_PROJECTS = {
    "System.Web": "src/WebFormsForCore.Web/WebFormsForCore.Web.csproj",
    "System.Configuration": "src/WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj",
    "Microsoft.Owin": "src/webForCore.Microsoft.Owin/webForCore.Microsoft.Owin.csproj",
    "Microsoft.Owin.Security": "src/webForCore.Microsoft.Owin.Security/webForCore.Microsoft.Owin.Security.csproj",
    "Microsoft.Owin.Security.Cookies": "src/webForCore.Microsoft.Owin.Security.Cookies/webForCore.Microsoft.Owin.Security.Cookies.csproj",
    "Microsoft.Owin.Security.OAuth": "src/webForCore.Microsoft.Owin.Security.OAuth/webForCore.Microsoft.Owin.Security.OAuth.csproj",
    "Microsoft.Owin.Host.SystemWeb": "src/webForCore.Microsoft.Owin.Host.SystemWeb/webForCore.Microsoft.Owin.Host.SystemWeb.csproj",
    "Microsoft.Owin.Hosting": "src/webForCore.Microsoft.Owin.Hosting/webForCore.Microsoft.Owin.Hosting.csproj",
    "Microsoft.Owin.Testing": "src/webForCore.Microsoft.Owin.Testing/webForCore.Microsoft.Owin.Testing.csproj",
    "Microsoft.Owin.Cors": "src/webForCore.Microsoft.Owin.Cors/webForCore.Microsoft.Owin.Cors.csproj",
    "Microsoft.Owin.Host.HttpListener": "src/webForCore.Microsoft.Owin.Host.HttpListener/webForCore.Microsoft.Owin.Host.HttpListener.csproj",
}

# Name -> how to lift it.
#   source: repository ("signalr"/"identity") and project folder; style "sdk" (globs) or "legacy" (explicit items).
#   refs: fork projects (FORK_PROJECTS keys) or other lifted projects (their Name); packages: (id, version).
PROJECTS = {
    "Microsoft.AspNet.SignalR.Core": dict(
        source=("signalr", "src/Microsoft.AspNet.SignalR.Core"), style="sdk",
        # Microsoft's build: SERVER (the server side of shared files), PERFCOUNTERS, and NET45, the framework it targets
        # (the #if NET45 branches are the .NET Framework code paths, which .NET has).
        defines="SERVER;PERFCOUNTERS;NET45;SIGNED", unsafe=True,
        refs=["Microsoft.Owin", "Microsoft.Owin.Security"],
        packages=[NEWTONSOFT, ("System.Diagnostics.PerformanceCounter", "11.0.0-rc.3.26522.148"),
                  ("System.Security.Cryptography.ProtectedData", "11.0.0-rc.3.26522.148")],
        # Not shipped to .NET Framework apps either: hubs.js comes from Scripts\hubs.js (its non-Windows path).
        resources={"Scripts/hubs.js": "Microsoft.AspNet.SignalR.Scripts.hubs.js"}),
    "Microsoft.AspNet.SignalR.SystemWeb": dict(
        source=("signalr", "src/Microsoft.AspNet.SignalR.SystemWeb"), style="sdk", defines="NET45;SIGNED",
        refs=["Microsoft.AspNet.SignalR.Core", "Microsoft.Owin", "Microsoft.Owin.Host.SystemWeb", "System.Web"]),
    # The .NET client, as its netstandard2.0 build (the target .NET runs); the gate and SignalR's tests use it.
    "Microsoft.AspNet.SignalR.Client": dict(
        source=("signalr", "src/Microsoft.AspNet.SignalR.Client"), style="sdk", defines="CLIENT;NETSTANDARD;NETSTANDARD2_0;SIGNED",
        packages=[NEWTONSOFT]),
    # SignalR's test infrastructure (hosts, test hubs and connections). Left out: IIS Express hosting (Windows); the
    # stress server's connection and hubs are compiled in (Microsoft's build references that project for them).
    "Microsoft.AspNet.SignalR.Tests.Common": dict(
        source=("signalr", "test/Microsoft.AspNet.SignalR.Tests.Common"), style="sdk", test=True, library=True,
        defines="NET45;SIGNED", exclude=["Infrastructure/IIS/**", "Infrastructure/IISExpressTestHost.cs"],
        extra_sources=[("src/Microsoft.AspNet.SignalR.StressServer/Connections/StressConnection.cs", "StressServer/StressConnection.cs"),
                       ("src/Microsoft.AspNet.SignalR.StressServer/Hubs/ConcurrentCallsHub.cs", "StressServer/ConcurrentCallsHub.cs"),
                       ("src/Microsoft.AspNet.SignalR.StressServer/Hubs/HubWithGroups.cs", "StressServer/HubWithGroups.cs"),
                       ("src/Microsoft.AspNet.SignalR.StressServer/Hubs/SimpleEchoHub.cs", "StressServer/SimpleEchoHub.cs")],
        refs=["Microsoft.AspNet.SignalR.Core", "Microsoft.AspNet.SignalR.Client", "Microsoft.AspNet.SignalR.SystemWeb", "Microsoft.Owin",
              "Microsoft.Owin.Security", "Microsoft.Owin.Security.Cookies", "Microsoft.Owin.Cors", "Microsoft.Owin.Hosting",
              "Microsoft.Owin.Testing", "Microsoft.Owin.Host.HttpListener", "Microsoft.Owin.Host.SystemWeb", "System.Web",
              "System.Configuration"],
        packages=[NEWTONSOFT, ("Owin", "1.0.0"), ("xunit.abstractions", "2.0.3"), ("xunit.assert", "2.4.0"),
                  ("xunit.extensibility.core", "2.4.0"), ("xunit.extensibility.execution", "2.4.0"), ("Moq", "4.21.0")],
        # As Microsoft's: the client's types (some shared with Core, such as WebSocketHandler) do not flow to the tests.
        private_refs=["Microsoft.AspNet.SignalR.Client"]),
    # The unit tests. Left out: the Service Bus scale-out's configuration facts (that scale-out is not lifted).
    "Microsoft.AspNet.SignalR.Tests": dict(
        source=("signalr", "test/Microsoft.AspNet.SignalR.Tests"), style="sdk", test=True, defines="NET45;SIGNED", unsafe=True,
        exclude=["Server/ServiceBusConfigurationFacts.cs"],
        refs=["Microsoft.AspNet.SignalR.Core", "Microsoft.AspNet.SignalR.Tests.Common", "Microsoft.Owin", "System.Web"],
        packages=[NEWTONSOFT, ("Owin", "1.0.0")],
        # Microsoft's xunit (2.4.0): later versions reject some of these tests' assertions (Assert.Throws on a Task).
        xunit="2.4.0",
        # .NET Framework's JIT, which these ran on, optimizes every method: a local is dead after its last use, which
        # the weak-reference tests (DefaultDependencyResolverFacts) rely on. .NET's tier-0 code keeps it alive.
        properties={"TieredCompilation": "false"}),
    "Microsoft.AspNet.Identity.Core": dict(
        source=("identity", "src/Microsoft.AspNet.Identity.Core"), style="legacy",
        packages=[("System.Security.Cryptography.ProtectedData", "11.0.0-rc.3.26522.148")]),
    "Microsoft.AspNet.Identity.EntityFramework": dict(
        source=("identity", "src/Microsoft.AspNet.Identity.EntityFramework"), style="legacy",
        refs=["Microsoft.AspNet.Identity.Core"], packages=[("EntityFramework", "6.5.1")]),
    "Microsoft.AspNet.Identity.Owin": dict(
        source=("identity", "src/Microsoft.AspNet.Identity.Owin"), style="legacy",
        refs=["Microsoft.AspNet.Identity.Core", "Microsoft.Owin", "Microsoft.Owin.Security", "Microsoft.Owin.Security.Cookies",
              "Microsoft.Owin.Security.OAuth"],
        packages=[("Owin", "1.0.0")]),
    "Identity.Test": dict(
        source=("identity", "test/Identity.Test"), style="legacy", test=True, assembly="Identity.Test",
        refs=["Microsoft.AspNet.Identity.Core", "Microsoft.AspNet.Identity.EntityFramework", "Microsoft.AspNet.Identity.Owin",
              "Microsoft.Owin", "Microsoft.Owin.Security", "Microsoft.Owin.Security.Cookies", "Microsoft.Owin.Security.OAuth",
              "Microsoft.Owin.Testing", "Microsoft.Owin.Hosting", "System.Configuration"],
        packages=[("EntityFramework", "6.5.1"), ("Owin", "1.0.0"), NEWTONSOFT, ("System.Data.SqlClient", "4.9.0")],
        copy=["App.config"],
        # xunit 1.9, which Microsoft ran these on, ran tests one at a time; they share one database.
        sequential=True,
        # LockoutEndToNowPlus5ShouldNotBeLockedOut assumes local time is UTC-8 ("UTC is 8 hours earlier"), as on the
        # machines it was written on: the suite runs in Pacific time.
        environment={"TZ": "America/Los_Angeles"}),
}

# (Name, relative path, exact old text, new text, expected count). Text uses "\n".
FIXUPS = [
    # .NET has no AppDomain.DefineDynamicAssembly; AssemblyBuilder's is the same call (a run-only dynamic assembly).
    ("Microsoft.AspNet.SignalR.Core", "Hubs/TypedClientBuilder.cs", "AppDomain.CurrentDomain.DefineDynamicAssembly(",
     "AssemblyBuilder.DefineDynamicAssembly(", 1),
    # .NET 11's ArraySegment<T> makes a null argument ambiguous between Message's string and ArraySegment<byte> overloads;
    # the string one is what Microsoft's build called.
    ("Microsoft.AspNet.SignalR.Core", "Messaging/MessageBusExtensions.cs", "new Message(acker, AckSubscriber.Signal, null)",
     "new Message(acker, AckSubscriber.Signal, (string)null)", 1),
    # Culture flows with the ExecutionContext on .NET (and .NET Framework 4.6+); the check that a thread-pool thread keeps
    # the default culture runs without the test's context, as it did on .NET Framework 4.5 (BCL difference).
    ("Microsoft.AspNet.SignalR.Tests", "TaskAsyncHelperFacts.cs", "                Task.Delay(100).ContinueWith(_ => saveCulture());\n",
     "                using (ExecutionContext.SuppressFlow()) // webForCore: culture flows with the ExecutionContext on .NET\n"
     "                {\n                    Task.Delay(100).ContinueWith(_ => saveCulture());\n                }\n", 1),
    # xunit 1.9 (Identity's tests) -> 2.x: Theory and InlineData moved from Xunit.Extensions into Xunit.
    ("Identity.Test", "*.cs", "using Xunit.Extensions;\n", "", None),
    # xunit 1.9's Assert.ThrowsDelegate is Action in xunit 2.
    ("Identity.Test", "ExceptionHelper.cs", "Assert.ThrowsDelegate", "Action", 4),
    # .NET's ArgumentException message ends "(Parameter 'name')"; .NET Framework's "\r\nParameter name: name" (BCL).
    ("Identity.Test", "ExceptionHelper.cs", r'"Value cannot be null or empty.\r\nParameter name: " + paramName',
     '"Value cannot be null or empty. (Parameter \'" + paramName + "\')" /* webForCore: .NET\'s message format */', 1),
    # EnsureDefaultSchemaWithInt opened the database without creating it, so it passed only when an earlier test had
    # created it; first in a run on a new server (CI's container) it failed. It creates it, as its sibling tests do.
    ("Identity.Test", "CustomIntKeyTest.cs",
     "            IdentityDbContextTest.VerifyDefaultSchema(new CustomUserContext());\n",
     "            // webForCore: create the database first; the test relied on an earlier test having created it.\n"
     "            Database.SetInitializer(new DropCreateDatabaseAlways<CustomUserContext>());\n"
     "            var db = new CustomUserContext();\n"
     "            db.Database.Initialize(true);\n"
     "            IdentityDbContextTest.VerifyDefaultSchema(db);\n", 1),
    # The tests' database: LocalDB is Windows-only; tools/lift/sqlserver.sh runs SQL Server 2022 in a container.
    ("Identity.Test", "App.config",
     r'connectionString="Data Source=(LocalDb)\v11.0;Initial Catalog=TestIdentityDb2.0-beta1;Integrated Security=SSPI"',
     'connectionString="Data Source=127.0.0.1,14333;Initial Catalog=TestIdentityDb2.0-beta1;User ID=sa;Password=webForCore-Test1!;TrustServerCertificate=True"', 1),
    ("Identity.Test", "App.config",
     '<defaultConnectionFactory type="System.Data.Entity.Infrastructure.LocalDbConnectionFactory, EntityFramework">\n'
     '      <parameters>\n        <parameter value="v11.0" />',
     '<defaultConnectionFactory type="System.Data.Entity.Infrastructure.SqlConnectionFactory, EntityFramework">\n'
     '      <parameters>\n        <parameter value="Data Source=127.0.0.1,14333;User ID=sa;Password=webForCore-Test1!;TrustServerCertificate=True" />', 1),
]


def commit(repo):
    return subprocess.run(["git", "-C", repo, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()


def sdk_items(project_dir, csproj, exclude=()):
    tree = open(csproj, encoding="utf-8-sig").read()
    removed = [r.replace("\\", "/") for r in re.findall(r'<Compile Remove="([^"]+)"', tree)] + list(exclude)
    compiles = []
    for path in sorted(glob.glob(os.path.join(project_dir, "**", "*.cs"), recursive=True)):
        rel = os.path.relpath(path, project_dir).replace("\\", "/")
        if rel.split("/")[0] in ("bin", "obj") or any(glob.fnmatch.fnmatch(rel, r.replace("**", "*")) for r in removed):
            continue
        compiles.append((path, rel))
    links = re.findall(r'<Compile Include="([^"]+)">\s*<Link>([^<]+)</Link>', tree) + re.findall(r'<Compile Include="([^"]+)" Link="([^"]+)"', tree)
    for inc, link in links:
        compiles.append((os.path.normpath(os.path.join(project_dir, inc.replace("\\", "/"))), link.replace("\\", "/")))
    resources = [(p, os.path.relpath(p, project_dir).replace("\\", "/")) for p in glob.glob(os.path.join(project_dir, "**", "*.resx"), recursive=True)]
    return compiles, resources


def legacy_items(project_dir, csproj):
    ns = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}
    root = ET.parse(csproj).getroot()
    compiles, resources = [], []
    for kind, out in (("Compile", compiles), ("EmbeddedResource", resources)):
        for item in root.iterfind(f".//m:{kind}", ns):
            inc = item.get("Include").replace("\\", "/")
            link = item.findtext("m:Link", namespaces=ns)
            out.append((os.path.normpath(os.path.join(project_dir, inc)), (link or inc).replace("\\", "/")))
    props = {e.tag.split("}")[1]: e.text for e in root.iterfind(".//m:PropertyGroup/*", ns)}
    return compiles, resources, props


def resource_name(rootns, rel, resx_full):
    designer = resx_full[:-len(".resx")] + ".Designer.cs"
    if os.path.exists(designer):
        m = re.search(r'ResourceManager\("([^"]+)"', open(designer, encoding="utf-8-sig").read())
        if m:
            return m.group(1) + ".resources"
    return rootns + "." + rel[:-len(".resx")].replace("/", ".") + ".resources"


def lift(name, cfg, roots, fork):
    repo_key, rel_dir = cfg["source"]
    repo = roots[repo_key]
    project_dir = os.path.join(repo, rel_dir)
    csproj = next(p for p in glob.glob(os.path.join(project_dir, "*.csproj")))
    if cfg["style"] == "sdk":
        compiles, resources = sdk_items(project_dir, csproj, cfg.get("exclude", []))
        compiles += [(os.path.join(repo, src), link) for src, link in cfg.get("extra_sources", [])]
        tree = open(csproj, encoding="utf-8-sig").read()
        rootns = (re.search(r"<RootNamespace>([^<]+)</RootNamespace>", tree) or [None, name])[1]
        assembly = name
    else:
        compiles, resources, props = legacy_items(project_dir, csproj)
        rootns = props.get("RootNamespace") or name
        assembly = props.get("AssemblyName") or name
    assembly = cfg.get("assembly", assembly)
    dest = os.path.join(fork, "test" if cfg.get("test") else "src", "webForCore." + name)
    for entry in os.listdir(dest) if os.path.isdir(dest) else []:
        if entry not in ("bin", "obj"):
            full = os.path.join(dest, entry)
            shutil.rmtree(full) if os.path.isdir(full) else os.remove(full)
    os.makedirs(dest, exist_ok=True)
    label = f"{repo_key} {commit(repo)}"
    manifest = [f"# {label}: source path (relative to its repository)\tdestination (relative to fork root)"]

    def copy(src, rel):
        target = os.path.join(dest, rel)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(src, target)
        manifest.append(f"{os.path.relpath(src, repo)}\t{os.path.relpath(target, fork)}")
        return target

    copied = [copy(src, rel) for src, rel in compiles]
    res_items = []
    for src, rel in resources:
        copy(src, rel)
        if rel.endswith(".resx"):
            res_items.append((rel, resource_name(rootns, rel, src)))
    for rel, logical in cfg.get("resources", {}).items():
        copy(os.path.join(project_dir, rel), rel)
        res_items.append((rel, logical))
    extra = [copy(os.path.join(project_dir, rel), rel) for rel in cfg.get("copy", [])]

    for fix_name, pattern, old, new, count in FIXUPS:
        if fix_name != name:
            continue
        hits = 0
        for target in copied + extra:
            if not glob.fnmatch.fnmatch(os.path.relpath(target, dest), pattern):
                continue
            raw = open(target, "rb").read()
            bom = raw.startswith(b"\xef\xbb\xbf")
            text = raw.decode("utf-8-sig")
            crlf = "\r\n" in text
            o, n = (old.replace("\n", "\r\n"), new.replace("\n", "\r\n")) if crlf else (old, new)
            if o in text:
                hits += text.count(o)
                open(target, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.replace(o, n).encode("utf-8"))
        assert count is None or hits == count, (name, pattern, old, hits)
        print(f"   fixup {pattern}: {hits}")

    refs = []
    for r in cfg.get("refs", []):
        path = FORK_PROJECTS.get(r) or (f"{'test' if PROJECTS[r].get('test') else 'src'}/webForCore.{r}/webForCore.{r}.csproj")
        refs.append(os.path.relpath(os.path.join(fork, path), dest).replace("\\", "/"))
    test_packages = TEST_PACKAGES
    if cfg.get("xunit"):
        # The adapter of xunit 2.4's time (3.x runs some 2.4 test cases it should skip; see lift_drawing_tests.py).
        test_packages = [(i, cfg["xunit"] if i == "xunit" else "2.4.5" if i == "xunit.runner.visualstudio" else v) for i, v in TEST_PACKAGES]
    packages = list(cfg.get("packages", [])) + (test_packages if cfg.get("test") and not cfg.get("library") else [])
    src_label = {"signalr": "SignalR/SignalR", "identity": "aspnet/AspNetIdentity"}[repo_key]
    licence = {"signalr": "Apache License 2.0; see LICENSE-APACHE-2.0.txt", "identity": "MIT License"}[repo_key]
    res_xml = "\n".join(f'    <EmbeddedResource Include="{rel}" LogicalName="{logical}" />' for rel, logical in res_items)
    private = {os.path.relpath(os.path.join(fork, FORK_PROJECTS.get(r) or f"src/webForCore.{r}/webForCore.{r}.csproj"), dest).replace("\\", "/")
               for r in cfg.get("private_refs", [])}
    refs += sorted(private - set(refs))
    ref_xml = "\n".join(f'    <ProjectReference Include="{r}"' + (' PrivateAssets="All"' if r in private else "") + " />" for r in refs)
    pkg_xml = "\n".join(f'    <PackageReference Include="{i}" Version="{v}" />' for i, v in packages)
    # The test host reads its configuration (connection strings, <entityFramework>) from testhost.dll.config.
    sequential = '    <Content Include="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />' if cfg.get("sequential") else ""
    if cfg.get("sequential"):
        open(os.path.join(dest, "xunit.runner.json"), "w").write('{\n  "parallelizeTestCollections": false\n}\n')
    run_settings = ""
    if cfg.get("environment"):
        variables = "".join(f"<{k}>{v}</{k}>" for k, v in cfg["environment"].items())
        open(os.path.join(dest, "test.runsettings"), "w").write(
            f'<?xml version="1.0" encoding="utf-8"?>\n<RunSettings>\n  <RunConfiguration>\n    <EnvironmentVariables>{variables}</EnvironmentVariables>\n  </RunConfiguration>\n</RunSettings>\n')
        run_settings = "\n    <RunSettingsFilePath>$(MSBuildProjectDirectory)/test.runsettings</RunSettingsFilePath>"
    app_config = '    <None Include="App.config" Link="testhost.dll.config" CopyToOutputDirectory="PreserveNewest" />' if "App.config" in cfg.get("copy", []) else ""
    key = os.path.relpath(os.path.join(fork, "src", "SigningKey", "35MSSharedLib1024.snk"), dest).replace("\\", "/")
    csproj_text = f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- Lifted from {src_label} {commit(repo)} ({rel_dir}) by tools/lift/lift_aspnet.py.
       {licence} and NOTICE at the repo root. Generated; do not edit. -->
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AssemblyName>{assembly}</AssemblyName>
    <RootNamespace>{rootns}</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <!-- The C# version of Microsoft's build: later ones resolve some calls differently (new Message(a, b, null) became
         ambiguous with the ArraySegment<byte> overload). -->
    <LangVersion>7.3</LangVersion>
    <DefineConstants>$(DefineConstants);{cfg.get("defines", "")}</DefineConstants>
    <AllowUnsafeBlocks>{str(cfg.get("unsafe", False)).lower()}</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591;CS0618;CS0612;CA1416;SYSLIB0011;SYSLIB0051;SYSLIB0014;SYSLIB0057;xUnit1013;xUnit1019;xUnit1031;xUnit2013;xUnit2014</NoWarn>
    <!-- Delay signed with Microsoft's public key, as Microsoft's build does: PublicKeyToken=31bf3856ad364e35. -->
    <SignAssembly>true</SignAssembly>
    <DelaySign>true</DelaySign>
    <AssemblyOriginatorKeyFile>{key}</AssemblyOriginatorKeyFile>
    <IsPackable>false</IsPackable>
    <IsTestProject>{str(bool(cfg.get("test")) and not cfg.get("library")).lower()}</IsTestProject>{run_settings}{"".join(f"{chr(10)}    <{k}>{v}</{k}>" for k, v in cfg.get("properties", {}).items())}
  </PropertyGroup>

  <Import Project="{os.path.relpath(os.path.join(fork, "src", "webForCore.WebStack.targets"), dest).replace(os.sep, "/")}" />

  <ItemGroup>
{chr(10).join(f'    <Compile Include="{os.path.relpath(t, dest)}" />' for t in copied)}
{res_xml}
{app_config}
{sequential}
  </ItemGroup>

  <ItemGroup>
{ref_xml}
{pkg_xml}
  </ItemGroup>
</Project>
"""
    open(os.path.join(dest, f"webForCore.{name}.csproj"), "w").write(csproj_text)
    os.makedirs(os.path.join(fork, "tools", "lift", "manifests"), exist_ok=True)
    open(os.path.join(fork, "tools", "lift", "manifests", name + ".tsv"), "w").write("\n".join(manifest) + "\n")
    print(f"{name}: {len(copied)} sources, {len(res_items)} resources")


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    roots = {"signalr": os.path.abspath(sys.argv[1]), "identity": os.path.abspath(sys.argv[2])}
    fork = os.path.abspath(sys.argv[3])
    for name in sys.argv[4:] or list(PROJECTS):
        lift(name, PROJECTS[name], roots, fork)


if __name__ == "__main__":
    main()
