#!/usr/bin/env python3
"""Lift ASP.NET Web Stack projects into the fork as real SDK-style projects.

For each project, MSBuild evaluates the source project (a spike-generated csproj for old-style
projects, or Microsoft's own SDK-style csproj) and lists its Compile and EmbeddedResource items.
Every file is copied into src/webForCore.<Name>/ at its original relative path (or its Link
path for shared files), and a csproj is written with explicit items. Each .resx gets an explicit
LogicalName: the base name its Designer.cs passes to ResourceManager when it uses a literal, or
the name Microsoft's build computes (RootNamespace + folder + file name). Assemblies are delay
signed with Microsoft's public key (tools/35MSSharedLib1024.snk), as Microsoft's own build does,
so they keep the PublicKeyToken 31bf3856ad364e35.

Usage: lift_project.py <WebStackRoot> <SpikeGeneratedDir> <ForkRoot> [Name ...]
Writes tools/lift/manifests/<Name>.tsv (source path, destination path) for LIFT.md.
"""
import json, os, re, shutil, subprocess, sys

# Name -> (source csproj relative to WebStack root or spike dir, target framework, folders that map to the project root)
PROJECTS = {
    "System.Net.Http.Formatting": dict(source="webstack:src/System.Net.Http.Formatting.ns2_0/System.Net.Http.Formatting.ns2_0.csproj",
                                       tfm="netstandard2.0", roots=["src/System.Net.Http.Formatting.ns2_0", "src/System.Net.Http.Formatting"]),
    "System.Web.Razor": dict(source="spike:System.Web.Razor", tfm="net11.0", roots=["src/System.Web.Razor"]),
    "System.Web.Http": dict(source="spike:System.Web.Http", tfm="net11.0", roots=["src/System.Web.Http"]),
    "System.Web.Cors": dict(source="spike:System.Web.Cors", tfm="net11.0", roots=["src/System.Web.Cors"]),
    "System.Web.Http.Cors": dict(source="spike:System.Web.Http.Cors", tfm="net11.0", roots=["src/System.Web.Http.Cors"]),
    # Test projects go to test/. Multi-targeting SDK projects are evaluated for their net8.0 item set.
    "Microsoft.TestCommon": dict(source="webstack:test/Microsoft.TestCommon/Microsoft.TestCommon.csproj", tfm="net11.0",
                                 roots=["test/Microsoft.TestCommon"], eval_tfm="net8.0", test=True, runner=False),
    "System.Net.Http.Formatting.Test": dict(source="webstack:test/System.Net.Http.Formatting.ns2_0.Test/System.Net.Http.Formatting.ns2_0.Test.csproj",
                                            tfm="net11.0", roots=["test/System.Net.Http.Formatting.ns2_0.Test", "test/System.Net.Http.Formatting.Test"],
                                            eval_tfm="net8.0", test=True),
    "System.Web.Razor.Test": dict(source="spike:System.Web.Razor.Test", tfm="net11.0", roots=["test/System.Web.Razor.Test"], test=True),
    "System.Web.Http.Test": dict(source="spike:System.Web.Http.Test", tfm="net11.0", roots=["test/System.Web.Http.Test"], test=True),
    "System.Web.Cors.Test": dict(source="spike:System.Web.Cors.Test", tfm="net11.0", roots=["test/System.Web.Cors.Test"], test=True),
    "System.Web.Http.Cors.Test": dict(source="spike:System.Web.Http.Cors.Test", tfm="net11.0", roots=["test/System.Web.Http.Cors.Test"], test=True),
}
# Test runner packages: Microsoft's (Test SDK 16.11, runner 2.4.5) predate .NET 11.
PACKAGE_VERSION_OVERRIDES = {"Microsoft.NET.Test.Sdk": "18.11.0", "xunit.runner.visualstudio": "3.1.5"}
DROP_PACKAGES = {"XunitXml.TestLogger"}
# Microsoft builds and tests the Web Stack on Windows, where its `* text` .gitattributes checks files out with CRLF.
# Multi-line string literals (in product code and tests) and Razor test baselines depend on that, so every text
# file is written with CRLF, as a Windows checkout has it. Binary types are those the Web Stack's .gitattributes marks binary.
BINARY_EXTENSIONS = {".bmp", ".dll", ".gif", ".jpg", ".png", ".snk", ".ico", ".pfx", ".zip"}

# Source edits applied after copying: (relative path, exact old text, new text). Each must match exactly once.
# Written with "\n"; matched against the CRLF file with "\r\n".
FIXUPS = {
    "System.Web.Http.Test": [
        ("Controllers/AuthenticationFilterResultTests.cs", "CancellationToken cancellationToken;\n",
         "CancellationToken cancellationToken = default; // webForCore: .NET reference assemblies expose the struct's private field (CS0165)\n", 3),
    ] + [
        ("Validation/Validators/DataAnnotationsModelValidatorTest.cs",
         f"[Fact]\n        public void {m}()\n        {{\n            AppDomainUtils.RunInSeparateAppDomain({m}_Inner);\n        }}",
         f'[Fact(Skip = "webForCore: needs AppDomainUtils.RunInSeparateAppDomain to isolate a static switch; no AppDomains on .NET 11 '
         f'(Microsoft runs this test on .NET Framework only)")]\n        public void {m}()\n        {{\n#if NETFRAMEWORK\n'
         f"            AppDomainUtils.RunInSeparateAppDomain({m}_Inner);\n#endif\n        }}", 1)
        for m in ("ValidateSetsMemberNameProperty_UsingDisplayName", "ValidateSetsMemberNameProperty_NotUsingDisplayName")
    ],
}

SDK_DEFINE = re.compile(r"^(TRACE|DEBUG|RELEASE|NET|NETCOREAPP|NETSTANDARD|NETFRAMEWORK)([0-9_]*(_OR_GREATER)?)?$")
KEY = "35MSSharedLib1024.snk"


def evaluate(csproj, tfm=None):
    cmd = ["dotnet", "msbuild", csproj, "-nologo", "-p:Configuration=Release", "-t:PrepareResourceNames",
           "-getItem:Compile", "-getItem:EmbeddedResource", "-getItem:ProjectReference", "-getItem:PackageReference",
           "-getItem:None", "-getProperty:RootNamespace", "-getProperty:AssemblyName", "-getProperty:DefineConstants",
           "-getProperty:NoWarn", "-getProperty:AllowUnsafeBlocks"]
    if tfm:
        cmd.append(f"-p:TargetFramework={tfm}")
    out = subprocess.run(cmd, capture_output=True, text=True, check=True).stdout
    return json.loads(out[out.index("{"):])


def project_dir(fork, name):
    return os.path.join(fork, "test" if PROJECTS.get(name, {}).get("test") else "src", f"webForCore.{name}")


def dest_rel(item, roots, webstack):
    # Files inside the original project keep their original relative path. (The SDK also gives them an automatic
    # Link of just the file name, because the spike project lives elsewhere, so Link must not win here.)
    full = item["FullPath"]
    for r in roots:
        base = os.path.join(webstack, r) + os.sep
        if full.startswith(base):
            return os.path.relpath(full, base).replace("\\", "/")
    if item.get("Link"):
        # Shared files (src/Common, CommonAssemblyInfo.cs) use the Link from Microsoft's csproj.
        # Formatting.ns2_0 builds links as "$(Prefix)\%(RecursiveDir)...", which can start with a separator.
        return item["Link"].replace("\\", "/").lstrip("/")
    return "Common/" + os.path.basename(full)


def designer_base_name(resx_full):
    designer = resx_full[:-len(".resx")] + ".Designer.cs"
    if os.path.exists(designer):
        m = re.search(r'ResourceManager\("([^"]+)"', open(designer, encoding="utf-8-sig").read())
        if m:
            return m.group(1)
    return None


def lift(name, cfg, webstack, spike, fork, commit):
    kind, path = cfg["source"].split(":", 1)
    src_csproj = os.path.join(webstack, path) if kind == "webstack" else os.path.join(spike, path, path + ".csproj")
    ev = evaluate(src_csproj, cfg.get("eval_tfm"))
    props, items = ev["Properties"], ev["Items"]
    dest_dir = project_dir(fork, name)
    if os.path.exists(dest_dir):
        shutil.rmtree(dest_dir)
    os.makedirs(dest_dir)
    manifest, compiles, resources, notes = [], [], [], []

    def copy(item):
        rel = dest_rel(item, cfg["roots"], webstack)
        dst = os.path.join(dest_dir, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        data = open(item["FullPath"], "rb").read()
        if os.path.splitext(dst)[1].lower() not in BINARY_EXTENSIONS and b"\0" not in data:
            data = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
        open(dst, "wb").write(data)
        shutil.copystat(item["FullPath"], dst)
        manifest.append((os.path.relpath(item["FullPath"], webstack), os.path.relpath(dst, fork)))
        return rel

    for c in items.get("Compile", []):
        compiles.append(copy(c))
    # PrepareResourceNames fills LogicalName for non-resx resources with names computed for the spike layout (wrong:
    # flattened to the file name). Keep a LogicalName only if the source project file declares it; otherwise MSBuild
    # computes it from the original layout restored here, as in Microsoft's build.
    src_text = open(src_csproj, encoding="utf-8-sig").read()
    declared = set(re.findall(r'LogicalName="([^"]+)"', src_text)) | set(re.findall(r"<LogicalName>([^<]+)</LogicalName>", src_text))
    for r in items.get("EmbeddedResource", []):
        rel = copy(r)
        logical = r.get("LogicalName") if r.get("LogicalName") in declared else None
        if not logical and rel.endswith(".resx"):
            literal = designer_base_name(r["FullPath"])
            computed = ".".join([props["RootNamespace"]] + [p for p in os.path.dirname(rel).split("/") if p] +
                                [os.path.basename(rel)[:-len(".resx")]])
            base = literal or computed
            logical = base + ".resources"
            notes.append(f"{rel}: LogicalName {logical} ({'from Designer.cs' if literal else 'RootNamespace + path'}; "
                         f"spike build named it {r.get('ManifestResourceName')})")
        resources.append((rel, logical))
    copied = []
    for n in items.get("None", []):
        if n.get("CopyToOutputDirectory"):
            copied.append((copy(n), n["CopyToOutputDirectory"]))

    for rel, old, new, expected in FIXUPS.get(name, []):
        path = os.path.join(dest_dir, rel)
        text = open(path, "rb").read().decode("utf-8-sig")
        old_crlf, new_crlf = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
        found = text.count(old_crlf)
        if found != expected:
            sys.exit(f"{name}: fixup for {rel} matched {found} times, expected {expected}")
        bom = open(path, "rb").read(3) == b"\xef\xbb\xbf"
        open(path, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(text.replace(old_crlf, new_crlf))
        notes.append(f"fixup {rel}: {found} edit(s)")

    refs = []
    for pr in items.get("ProjectReference", []):
        full = pr["FullPath"]
        if full.startswith(os.path.join(fork, "src") + os.sep):
            refs.append(os.path.relpath(full, dest_dir))
        else:
            sib = os.path.basename(full)[:-len(".csproj")].replace(".ns2_0", "")
            target = os.path.join(project_dir(fork, sib), f"webForCore.{sib}.csproj")
            refs.append(os.path.relpath(target, dest_dir))
    pkgs = [(p["Identity"], PACKAGE_VERSION_OVERRIDES.get(p["Identity"], p["Version"]))
            for p in items.get("PackageReference", []) if p.get("Version") and p["Identity"] not in DROP_PACKAGES]
    if cfg.get("test") and cfg.get("runner", True):
        pkgs += [(i, v) for i, v in PACKAGE_VERSION_OVERRIDES.items() if i not in {p for p, _ in pkgs}]
    defines = [d for d in props["DefineConstants"].split(";") if d and not SDK_DEFINE.match(d)]
    nowarn = "$(NoWarn);CS1591;CS0618;CS0612;SYSLIB0003;SYSLIB0011;SYSLIB0051"
    shared_targets = os.path.relpath(os.path.join(fork, "src", "webForCore.WebStack.targets"), dest_dir)

    x = ['<Project Sdk="Microsoft.NET.Sdk">', "",
         f"  <!-- Lifted from aspnet/AspNetWebStack {commit} ({cfg['source'].split(':', 1)[1]}) by tools/lift/lift_project.py.",
         "       Apache License 2.0; see LICENSE-APACHE-2.0.txt and NOTICE at the repo root. -->",
         "  <PropertyGroup>",
         f"    <TargetFramework>{cfg['tfm']}</TargetFramework>",
         f"    <AssemblyName>{props['AssemblyName']}</AssemblyName>",
         f"    <RootNamespace>{props['RootNamespace']}</RootNamespace>",
         f"    <PackageId>webForCore.{name[len('System.'):] if name.startswith('System.') else name}</PackageId>",
         "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
         "    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>",
         "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
         "    <ImplicitUsings>disable</ImplicitUsings>",
         "    <Nullable>disable</Nullable>",
         "    <!-- Microsoft's build uses C# 9 (tools/WebStack.settings.targets). -->",
         "    <LangVersion>9.0</LangVersion>",
         "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
         f"    <NoWarn>{nowarn}</NoWarn>",
         f"    <DefineConstants>$(DefineConstants);{';'.join(defines)}</DefineConstants>",
         "    <!-- Delay signed with Microsoft's public key, as Microsoft's build does: PublicKeyToken=31bf3856ad364e35. -->",
         "    <SignAssembly>true</SignAssembly>",
         "    <DelaySign>true</DelaySign>",
         f"    <AssemblyOriginatorKeyFile>{os.path.relpath(os.path.join(fork, 'src', 'SigningKey', KEY), dest_dir)}</AssemblyOriginatorKeyFile>"]
    if props.get("AllowUnsafeBlocks", "").lower() == "true":
        x.append("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>")
    if cfg.get("test"):
        x += ["    <IsPackable>false</IsPackable>"]
        if cfg.get("runner", True):
            x += ["    <IsTestProject>true</IsTestProject>"]
    x += ["  </PropertyGroup>", "", f'  <Import Project="{shared_targets}" />', "", "  <ItemGroup>"]
    x += [f'    <Compile Include="{c}" />' for c in sorted(compiles)]
    x += ["  </ItemGroup>", "", "  <ItemGroup>"]
    for rel, logical in sorted(resources):
        x.append(f'    <EmbeddedResource Include="{rel}"' + (f' LogicalName="{logical}"' if logical else "") + " />")
    for rel, mode in sorted(copied):
        x.append(f'    <None Include="{rel}" CopyToOutputDirectory="{mode}" />')
    x.append("  </ItemGroup>")
    if refs or pkgs:
        x += ["", "  <ItemGroup>"]
        x += [f'    <ProjectReference Include="{r}" />' for r in refs]
        x += [f'    <PackageReference Include="{i}" Version="{v}" />' for i, v in pkgs]
        x.append("  </ItemGroup>")
    x += ["</Project>", ""]
    with open(os.path.join(dest_dir, f"webForCore.{name}.csproj"), "w", encoding="utf-8") as f:
        f.write("\n".join(x))

    mdir = os.path.join(fork, "tools", "lift", "manifests")
    os.makedirs(mdir, exist_ok=True)
    with open(os.path.join(mdir, f"{name}.tsv"), "w", encoding="utf-8") as f:
        f.write(f"# aspnet/AspNetWebStack {commit}: source path (relative to repo root)\tdestination (relative to fork root)\n")
        f.writelines(f"{a}\t{b}\n" for a, b in sorted(manifest))
    print(f"{name}: {len(compiles)} Compile, {len(resources)} EmbeddedResource, {len(refs)} ProjectReference, "
          f"{len(pkgs)} PackageReference, defines={defines}")
    for n in notes:
        print(f"   {n}")


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    webstack, spike, fork = (os.path.abspath(a) for a in sys.argv[1:4])
    commit = subprocess.run(["git", "-C", webstack, "rev-parse", "--short", "HEAD"], capture_output=True, text=True).stdout.strip()
    names = sys.argv[4:] or list(PROJECTS)
    key_dst = os.path.join(fork, "src", "SigningKey", KEY)
    if not os.path.exists(key_dst):
        shutil.copy2(os.path.join(webstack, "tools", KEY), key_dst)
    for n in names:
        lift(n, PROJECTS[n], webstack, spike, fork, commit)


if __name__ == "__main__":
    main()
