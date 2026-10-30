#!/usr/bin/env python3
"""Generate SDK-style net11.0 csproj files from the ASP.NET Web Stack's old-style csproj files.

The original source files are NOT copied. Each generated project points at them by relative path,
so the Web Stack checkout stays pristine and can be updated with `git pull`.

Usage: gen_csproj.py <WebStackRoot> <OutDir> [ProjectName ...]
Writes <OutDir>/<Name>/<Name>.csproj and prints a report of unmapped references.
"""
import os, re, sys
import xml.etree.ElementTree as ET

DEFAULT_PROJECTS = [
    "System.Web.Razor", "System.Web.Http", "System.Web.Cors",
    "System.Web.Http.Cors", "System.Web.Http.Owin", "System.Web.Http.WebHost", "System.Web.Helpers",
    "System.Web.WebPages.Deployment", "System.Web.WebPages", "System.Web.WebPages.Razor", "System.Web.Mvc", "Microsoft.Web.Mvc",
]

TARGET_FRAMEWORK = "net11.0"

# NuGet id prefix for the project. Upstream owns the WebFormsForCore.* ids on nuget.org, so ours must differ.
PACKAGE_PREFIX = "webForCore"

# Framework references -> what they become on net11.0 with the fork.
WFC = "$(WfcRoot)/src"
PROJECT_MAP = {
    "System.Web": f"{WFC}/WebFormsForCore.Web/WebFormsForCore.Web.csproj",
    "System.Web.Abstractions": f"{WFC}/WebFormsForCore.Web/WebFormsForCore.Web.csproj",
    "System.Web.Routing": f"{WFC}/WebFormsForCore.Web/WebFormsForCore.Web.csproj",
    "System.Web.Extensions": f"{WFC}/WebFormsForCore.Web.Extensions/WebFormsForCore.Web.Extensions.csproj",
    "System.Web.Services": f"{WFC}/WebFormsForCore.Web.Services/WebFormsForCore.Web.Services.csproj",
    "System.Drawing": f"{WFC}/WebFormsForCore.Drawing/WebFormsForCore.Drawing.csproj",
    "System.configuration": f"{WFC}/WebFormsForCore.Configuration/WebFormsForCore.Configuration.csproj",
    "Microsoft.Web.Infrastructure": f"{WFC}/WebFormsForCore.Web.Infrastructure/WebFormsForCore.Web.Infrastructure.csproj",
}
PROJECT_MAP["System.Web.DataVisualization"] = f"{WFC}/WebFormsForCore.Web.DataVisualization/WebFormsForCore.Web.DataVisualization.csproj"
# Temporary: Katana comes from NuGet (net45, restores with NU1701) until it is lifted.
# Versions checked on nuget.org 29 Oct 2026 (Microsoft.Owin latest is 4.2.3; the handoff's 4.2.2 was from memory).
PACKAGE_MAP = {"System.Runtime.Caching": ("System.Runtime.Caching", "11.0.0-rc.3.26522.148"),
               "Microsoft.Owin": ("Microsoft.Owin", "4.2.3"), "Owin": ("Owin", "1.0.0")}
# Types that were in .NET Framework's System.dll but are a separate package on modern .NET.
# Every Web Stack project references "System", so every generated project gets these.
SYSTEM_DLL_PACKAGES = [("System.CodeDom", "11.0.0-rc.3.26522.148")]
BUILTIN = {"system", "system.core", "system.xml", "system.xml.linq", "system.data", "system.net.http",
           "system.componentmodel.dataannotations", "system.runtime.serialization", "microsoft.csharp",
           "system.net.http.webrequest", "windowsbase", "system.identitymodel", "system.transactions",
           "netstandard", "system.buffers", "system.memory", "system.threading.tasks.extensions",
           "newtonsoft.json", "newtonsoft.json.bson"}
SKIP_PACKAGES = re.compile(r"^(Microsoft\.AspNet\.|Microsoft\.Web\.Infrastructure|Microsoft\.Owin|Owin$|EntityFramework|Microsoft\.Bcl|Microsoft\.Net\.Http|StyleCop|xunit\.runner\.visualstudio$)", re.I)

# Test projects (test/<Name>) run with `dotnet test`: xunit v2 tests through the VSTest adapter.
TEST_PACKAGES = [("Microsoft.NET.Test.Sdk", "18.11.0"), ("xunit.runner.visualstudio", "3.1.5")]
# SDK-style Web Stack test projects that old-style test projects reference.
SDK_TEST_PROJECTS = {"Microsoft.TestCommon": "test/Microsoft.TestCommon/Microsoft.TestCommon.csproj",
                     "System.Net.Http.Formatting.Test": "test/System.Net.Http.Formatting.ns2_0.Test/System.Net.Http.Formatting.ns2_0.Test.csproj"}


def load(path):
    text = open(path, encoding="utf-8-sig").read()
    text = re.sub(r'\sxmlns="[^"]+"', "", text, count=1)
    return ET.fromstring(text)


def rel(from_dir, target):
    return os.path.relpath(target, from_dir).replace("\\", "/")


def generate(root, out_dir, name, report):
    is_test = not os.path.isdir(os.path.join(root, "src", name)) and os.path.isdir(os.path.join(root, "test", name))
    proj_dir = os.path.join(root, "test" if is_test else "src", name)
    src_csproj = os.path.join(proj_dir, name + ".csproj")
    if not os.path.exists(src_csproj):
        report.append(f"{name}: csproj not found at {src_csproj}")
        return
    tree = load(src_csproj)
    gen_dir = os.path.join(out_dir, name)
    os.makedirs(gen_dir, exist_ok=True)

    asm, rootns, defines = name, name, []
    for pg in tree.iter("PropertyGroup"):
        if pg.get("Condition"):
            continue
        asm = (pg.findtext("AssemblyName") or asm)
        rootns = (pg.findtext("RootNamespace") or rootns)
        dc = pg.findtext("DefineConstants")
        if dc:
            defines += [d for d in dc.split(";") if d and not d.startswith("$(")]

    compiles, resources, project_refs, pkg_refs, unmapped = [], [], [], [], []
    for item in tree.iter("Compile"):
        inc = item.get("Include")
        if inc:
            compiles.append((inc.replace("\\", "/"), item.findtext("Link")))
    for item in tree.iter("EmbeddedResource"):
        inc = item.get("Include")
        if inc:
            resources.append((inc.replace("\\", "/"), item.findtext("Link"), item.findtext("LogicalName"),
                              item.findtext("DependentUpon")))
    for item in tree.iter("Reference"):
        ref = (item.get("Include") or "").split(",")[0].strip()
        pmap = {k.lower(): v for k, v in PROJECT_MAP.items()}
        kmap = {k.lower(): v for k, v in PACKAGE_MAP.items()}
        if ref.lower() in pmap:
            if pmap[ref.lower()] not in project_refs:
                project_refs.append(pmap[ref.lower()])
        elif ref.lower() in kmap:
            pkg_refs.append(kmap[ref.lower()])
        elif ref.lower() == "system":
            pkg_refs.extend(p for p in SYSTEM_DLL_PACKAGES if p not in pkg_refs)
        elif ref.lower() in BUILTIN:
            pass
        elif ref:
            unmapped.append(ref)
    for item in tree.iter("ProjectReference"):
        inc = (item.get("Include") or "").replace("\\", "/")
        sibling = os.path.basename(inc)[:-len(".csproj")]
        if sibling == "System.Net.Http.Formatting":
            # Microsoft's own netstandard2.0 build; consumable from net11.0 as-is.
            ns = os.path.join(root, "src", "System.Net.Http.Formatting.ns2_0", "System.Net.Http.Formatting.ns2_0.csproj")
            project_refs.append(rel(gen_dir, ns))
        elif sibling in SDK_TEST_PROJECTS:
            project_refs.append(rel(gen_dir, os.path.join(root, SDK_TEST_PROJECTS[sibling])))
        else:
            project_refs.append(f"../{sibling}/{sibling}.csproj")
    pc = os.path.join(proj_dir, "packages.config")
    if os.path.exists(pc):
        for p in load(pc).iter("package"):
            pid, ver = p.get("id"), p.get("version")
            if SKIP_PACKAGES.match(pid):
                continue
            pkg_refs.append((pid, ver))
            report.append(f"{name}: package {pid} {ver} copied from packages.config (check it supports {TARGET_FRAMEWORK})")
    # References that a packages.config entry provides (xunit.*, Moq, Castle.Core, ...) are not gaps.
    from_packages = {p.lower() for p, _ in pkg_refs}
    unmapped = [u for u in unmapped if u.lower() not in from_packages and not u.lower().startswith("xunit.")]
    for u in unmapped:
        report.append(f"{name}: UNMAPPED reference '{u}' (no .NET 11 equivalent mapped; expect compile errors)")
    if is_test:
        pkg_refs.extend(p for p in TEST_PACKAGES if p not in pkg_refs)

    out = ['<Project Sdk="Microsoft.NET.Sdk">', "  <PropertyGroup>",
           f"    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>",
           f"    <AssemblyName>{asm}</AssemblyName>", f"    <RootNamespace>{rootns}</RootNamespace>",
           # Only "System." is dropped: dropping "Microsoft." too made Microsoft.Web.Mvc and System.Web.Mvc collide.
           f"    <PackageId>{PACKAGE_PREFIX}.{name[len('System.'):] if name.startswith('System.') else name}</PackageId>",
           "    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>",
           "    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>",
           "    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>",
           "    <ImplicitUsings>disable</ImplicitUsings>", "    <Nullable>disable</Nullable>",
           "    <LangVersion>latest</LangVersion>", "    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>",
           "    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>",
           "    <NoWarn>$(NoWarn);CS1591;CS0618;CS0612;SYSLIB0003;SYSLIB0011;SYSLIB0051</NoWarn>",
           f"    <DefineConstants>$(DefineConstants);{';'.join(sorted(set(defines)))}</DefineConstants>"]
    if is_test:
        out += ["    <IsTestProject>true</IsTestProject>", "    <IsPackable>false</IsPackable>"]
    out += ["  </PropertyGroup>", "  <ItemGroup>"]
    for inc, link in compiles:
        target = os.path.normpath(os.path.join(proj_dir, inc))
        attr = f' Link="{link.replace(chr(92), "/")}"' if link else ""
        out.append(f'    <Compile Include="{rel(gen_dir, target)}"{attr} />')
    for inc, link, logical, dep in resources:
        target = os.path.normpath(os.path.join(proj_dir, inc))
        attrs = f' Link="{link.replace(chr(92), "/")}"' if link else ""
        if logical:
            attrs += f' LogicalName="{logical}"'
        out.append(f'    <EmbeddedResource Include="{rel(gen_dir, target)}"{attrs} />')
    out.append("  </ItemGroup>")
    if project_refs or pkg_refs:
        out.append("  <ItemGroup>")
        for pr in project_refs:
            out.append(f'    <ProjectReference Include="{pr}" />')
        for pid, ver in pkg_refs:
            out.append(f'    <PackageReference Include="{pid}" Version="{ver}" />')
        out.append("  </ItemGroup>")
    out.append("</Project>")
    with open(os.path.join(gen_dir, name + ".csproj"), "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    report.append(f"{name}: {len(compiles)} Compile, {len(resources)} EmbeddedResource, "
                  f"{len(project_refs)} ProjectReference, {len(pkg_refs)} PackageReference")


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    root, out_dir = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
    names = sys.argv[3:] or DEFAULT_PROJECTS
    report = []
    for n in names:
        generate(root, out_dir, n, report)
    print("\n".join(report))


if __name__ == "__main__":
    main()
