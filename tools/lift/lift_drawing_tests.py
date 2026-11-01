#!/usr/bin/env python3
"""Lift System.Drawing.Common 6.0's own test suite (dotnet/runtime release/6.0, MIT) to run against the fork's
System.Drawing.Common (Microsoft's managed code on SkiaGdiPlus; see lift_drawing.py).

Four projects under test/, each copied byte for byte, then the FIXUPS below are applied:
  - webForCore.Microsoft.DotNet.XUnitExtensions: dotnet/arcade release/6.0, src/Microsoft.DotNet.XUnitExtensions
    (ConditionalFact/Theory, ActiveIssue, PlatformSpecific, ...). dotnet/runtime consumes it as a package from the
    dotnet-eng feed; building it from source keeps the fork on nuget.org.
  - webForCore.Microsoft.DotNet.RemoteExecutor: dotnet/arcade release/6.0, src/Microsoft.DotNet.RemoteExecutor.
  - webForCore.TestUtilities: dotnet/runtime src/libraries/Common/tests/TestUtilities (PlatformDetection,
    AssertExtensions, ...) with the Common interop files its csproj links.
  - webForCore.System.Drawing.Common.Tests: the compile list of System.Drawing.Common.Tests.csproj for
    $(NetCoreAppCurrent), its $(CommonTestPath) files, and the test data packages it references
    (System.Drawing.Common.TestData, System.ComponentModel.TypeConverter.TestData,
    System.Windows.Extensions.TestData), taken from dotnet/runtime-assets release/6.0 and copied to the output
    directory as the packages' contentFiles are.
Test selection is upstream's: ActiveIssue/PlatformSpecific/... add xunit traits (category=failing, nonlinuxtests, ...),
and dotnet/runtime's Unix runs filter them out. tools/lift/test-results reports both that selection and the
category=failing tests (mostly libgdiplus differences from Windows that Microsoft disabled on Unix).

Usage: lift_drawing_tests.py <dotnet-runtime-6.0> <arcade-6.0> <runtime-assets> <ForkRoot>
Writes tools/lift/manifests/System.Drawing.Common.Tests.tsv.
"""
import os, re, shutil, subprocess, sys
import xml.etree.ElementTree as ET

NAME = "System.Drawing.Common.Tests"
TESTDATA = ["System.Drawing.Common.TestData", "System.ComponentModel.TypeConverter.TestData",
            "System.Windows.Extensions.TestData"]

# (source-relative path, old, new, expected count). Text uses "\n"; files are matched in their own line endings.
# The fork's GDI+ is managed (SkiaGdiPlus): the suite's "is GDI+ available" checks probe for libgdiplus, and its
# version and distribution checks only screen out old libgdiplus builds. Each now answers for SkiaGdiPlus.
FIXUPS = [
    ("src/libraries/Common/tests/TestUtilities/System/PlatformDetection.cs",
     '                       return NativeLibrary.TryLoad("libgdiplus.so", out _) || NativeLibrary.TryLoad("libgdiplus.so.0", out _);\n',
     '                       return true; // webForCore: GDI+ is managed (SkiaGdiPlus on SkiaSharp); there is no libgdiplus to probe.\n', 1),
    ("src/libraries/Common/tests/System/Drawing/Helpers.cs",
     "            if (PlatformDetection.IsWindows)\n            {\n                return true;\n            }\n\n            Version installedVersion;\n",
     "            if (PlatformDetection.IsWindows || true) // webForCore: GDI+ is SkiaGdiPlus, not libgdiplus (no version to check)\n"
     "            {\n                return true;\n            }\n\n            Version installedVersion;\n", 1),
    ("src/libraries/Common/tests/System/Drawing/Helpers.cs",
     "            if (PlatformDetection.IsRedHatFamily || PlatformDetection.IsFedora || PlatformDetection.IsOpenSUSE)\n",
     "            if (false) // webForCore: screened out these distributions' old libgdiplus; GDI+ is SkiaGdiPlus here\n", 1),
    ("src/libraries/Common/tests/System/Drawing/Helpers.cs",
     "            if (PlatformDetection.IsRedHatFamily)\n",
     "            if (false) // webForCore: screened out RedHat's old libgdiplus; GDI+ is SkiaGdiPlus here\n", 2),
    # On Windows too, GDI+ is SkiaGdiPlus, not Windows' own: the suite's Windows checks choose Windows GDI+'s
    # expectations, so Windows answers as Linux and both run the same tests with the same expectations (#46).
    ("src/libraries/Common/tests/TestUtilities/System/PlatformDetection.Windows.cs",
     "        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);\n",
     "        public static bool IsWindows => false; // webForCore: GDI+ is SkiaGdiPlus on Windows too; Windows runs as Linux (#46)\n", 1),
    ("src/Microsoft.DotNet.XUnitExtensions/src/DiscovererHelpers.cs",
     "                (platforms.HasFlag(TestPlatforms.Linux) && RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) ||\n",
     "                // webForCore: GDI+ is SkiaGdiPlus on Windows too; Windows runs as Linux (#46)\n"
     "                (platforms.HasFlag(TestPlatforms.Linux) && (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))) ||\n", 1),
    ("src/Microsoft.DotNet.XUnitExtensions/src/DiscovererHelpers.cs",
     "                (platforms.HasFlag(TestPlatforms.Windows) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows));\n",
     "                false; // webForCore: (TestPlatforms.Windows) Windows runs as Linux, above\n", 1),
    ("src/libraries/System.Drawing.Common/tests/BitmapTests.cs",
     "                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))\n",
     "                if (PlatformDetection.IsWindows) // webForCore: Windows runs as Linux (#46)\n", 1),
    ("src/libraries/System.Drawing.Common/tests/Imaging/MetafileTests.cs",
     "            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))\n",
     "            if (PlatformDetection.IsWindows) // webForCore: Windows runs as Linux (#46)\n", 1),
]


def commit_of(repo):
    return subprocess.check_output(["git", "-C", repo, "rev-parse", "--short", "HEAD"], text=True).strip()


class Lift:
    def __init__(self, fork):
        self.fork = fork
        self.manifest = []
        self.fixups = list(FIXUPS)

    def copy(self, repo, label, source_rel, dest_abs):
        os.makedirs(os.path.dirname(dest_abs), exist_ok=True)
        shutil.copyfile(os.path.join(repo, source_rel), dest_abs)
        self.manifest.append(f"{label}\t{source_rel}\t{os.path.relpath(dest_abs, self.fork)}")
        mine = [f for f in self.fixups if f[0] == source_rel and label.startswith(("dotnet/runtime ", "dotnet/arcade "))]
        if mine:
            raw = open(dest_abs, "rb").read()
            bom = raw.startswith(b"\xef\xbb\xbf")
            text = raw.decode("utf-8-sig")
            crlf = "\r\n" in text
            for fix in mine:
                old, new, n = fix[1], fix[2], fix[3]
                if crlf:
                    old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
                assert text.count(old) == n, (source_rel, old, text.count(old))
                text = text.replace(old, new)
                self.fixups.remove(fix)
                print(f"   fixup {source_rel}")
            open(dest_abs, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))


def compile_items(csproj, keep_group):
    items = []
    for group in ET.parse(csproj).getroot().findall("ItemGroup"):
        if not keep_group(group.get("Condition", "")):
            continue
        for item in group.findall("Compile"):
            items.append(item.get("Include").replace("\\", "/"))
    return items


# Clears a lifted project, keeping its build output (bin/, obj/) so a rerun does not break a build or test in progress.
def reset(path):
    os.makedirs(path, exist_ok=True)
    for entry in os.listdir(path):
        if entry in ("bin", "obj"):
            continue
        full = os.path.join(path, entry)
        shutil.rmtree(full) if os.path.isdir(full) else os.remove(full)


# dotnet/runtime 6.0 builds these with xunit 2.4.2-pre.9 (eng/Versions.props), which is not on nuget.org; 2.4.2 adds
# Assert.Equal(float, float, float), which makes the suite's Assert.Equal(float, float, int) calls ambiguous, so 2.4.1. Later
# xunit.assert versions removed API the suite uses (AssertActualExpectedException, ThrowsException(Type)).
XUNIT_VERSION = "2.4.1"
XUNIT = f"""    <PackageReference Include="xunit.abstractions" Version="2.0.3" />
    <PackageReference Include="xunit.assert" Version="{XUNIT_VERSION}" />
    <PackageReference Include="xunit.extensibility.core" Version="{XUNIT_VERSION}" />
    <PackageReference Include="xunit.extensibility.execution" Version="{XUNIT_VERSION}" />"""

COMMON_PROPS = """    <TargetFramework>net11.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>"""


def main():
    runtime, arcade, assets, fork = (os.path.abspath(a) for a in sys.argv[1:5])
    rt, ar, ra = (f"dotnet/runtime {commit_of(runtime)}", f"dotnet/arcade {commit_of(arcade)}",
                  f"dotnet/runtime-assets {commit_of(assets)}")
    lift = Lift(fork)
    test = os.path.join(fork, "test")

    # 1. Microsoft.DotNet.XUnitExtensions
    name = "webForCore.Microsoft.DotNet.XUnitExtensions"
    dest = os.path.join(test, name)
    reset(dest)
    src_rel = "src/Microsoft.DotNet.XUnitExtensions/src"
    for root, _, files in os.walk(os.path.join(arcade, src_rel)):
        for f in sorted(files):
            if f.endswith(".cs"):
                rel = os.path.relpath(os.path.join(root, f), arcade)
                lift.copy(arcade, ar, rel, os.path.join(dest, os.path.relpath(rel, src_rel)))
    open(os.path.join(dest, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- Microsoft.DotNet.XUnitExtensions ({ar}, release/6.0), lifted by tools/lift/lift_drawing_tests.py. MIT.
       Generated; do not edit. -->
  <PropertyGroup>
{COMMON_PROPS}
    <AssemblyName>Microsoft.DotNet.XUnitExtensions</AssemblyName>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="**/*.cs" Exclude="bin/**;obj/**" />
  </ItemGroup>
  <ItemGroup>
{XUNIT}
  </ItemGroup>
</Project>
""")

    # 2. Microsoft.DotNet.RemoteExecutor (a library and the executable it starts)
    name = "webForCore.Microsoft.DotNet.RemoteExecutor"
    dest = os.path.join(test, name)
    reset(dest)
    src_rel = "src/Microsoft.DotNet.RemoteExecutor/src"
    for f in sorted(os.listdir(os.path.join(arcade, src_rel))):
        if f.endswith(".cs"):
            lift.copy(arcade, ar, f"{src_rel}/{f}", os.path.join(dest, f))
    open(os.path.join(dest, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- Microsoft.DotNet.RemoteExecutor ({ar}, release/6.0), lifted by tools/lift/lift_drawing_tests.py. MIT.
       Generated; do not edit. -->
  <PropertyGroup>
{COMMON_PROPS}
    <OutputType>Exe</OutputType>
    <AssemblyName>Microsoft.DotNet.RemoteExecutor</AssemblyName>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="xunit.assert" Version="{XUNIT_VERSION}" />
    <!-- As arcade's csproj: ClrMD, for the stack dump RemoteInvokeHandle writes when a remote process times out. -->
    <PackageReference Include="Microsoft.Diagnostics.Runtime" Version="1.0.5" />
  </ItemGroup>
</Project>
""")

    # 3. TestUtilities
    name = "webForCore.TestUtilities"
    dest = os.path.join(test, name)
    reset(dest)
    tu_rel = "src/libraries/Common/tests/TestUtilities"
    for inc in compile_items(os.path.join(runtime, tu_rel, "TestUtilities.csproj"), lambda c: c == ""):
        if inc.startswith("$(CommonPath)"):
            rel = "src/libraries/Common/src/" + inc[len("$(CommonPath)"):]
            lift.copy(runtime, rt, rel, os.path.join(dest, "Common", inc[len("$(CommonPath)"):]))
        else:
            lift.copy(runtime, rt, f"{tu_rel}/{inc}", os.path.join(dest, inc))
    open(os.path.join(dest, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- TestUtilities ({rt}, release/6.0, src/libraries/Common/tests/TestUtilities), lifted by
       tools/lift/lift_drawing_tests.py. MIT. Generated; do not edit. -->
  <PropertyGroup>
{COMMON_PROPS}
    <AssemblyName>TestUtilities</AssemblyName>
    <DefineConstants>$(DefineConstants);NETCOREAPP</DefineConstants>
    <IsTestProject>false</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="**/*.cs" Exclude="bin/**;obj/**" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../webForCore.Microsoft.DotNet.XUnitExtensions/webForCore.Microsoft.DotNet.XUnitExtensions.csproj" />
{XUNIT}
  </ItemGroup>
</Project>
""")

    # 4. System.Drawing.Common.Tests
    name = "webForCore.System.Drawing.Common.Tests"
    dest = os.path.join(test, name)
    reset(dest)
    t_rel = "src/libraries/System.Drawing.Common/tests"
    csproj = os.path.join(runtime, t_rel, "System.Drawing.Common.Tests.csproj")
    items = compile_items(csproj, lambda c: c in ("", "$(TargetFramework.StartsWith('$(NetCoreAppCurrent)'))"))
    for inc in items:
        if inc.startswith("$(CommonTestPath)"):
            rel = inc[len("$(CommonTestPath)"):]
            lift.copy(runtime, rt, "src/libraries/Common/tests/" + rel, os.path.join(dest, "Common", rel))
        else:
            lift.copy(runtime, rt, f"{t_rel}/{inc}", os.path.join(dest, inc))
    resources = []
    for res in ET.parse(csproj).getroot().iter("EmbeddedResource"):
        inc = res.get("Include").replace("\\", "/")
        rel = inc.split("contentFiles/any/any/", 1)[1]
        resources.append((rel, res.findtext("LogicalName")))
    data = 0
    for pkg in TESTDATA:
        base = f"src/{pkg}"
        for root, _, files in os.walk(os.path.join(assets, base)):
            for f in sorted(files):
                if f.endswith(".csproj"):
                    continue
                rel = os.path.relpath(os.path.join(root, f), os.path.join(assets, base))
                lift.copy(assets, ra, f"{base}/{rel}", os.path.join(dest, "TestData", rel))
                data += 1
    # Code New Roman's own licence text both allows (SIL OFL 1.1) and forbids redistribution, so the fonts are copied
    # for local runs but kept out of git; rerun this script after a fresh clone to restore them.
    open(os.path.join(dest, "TestData", ".gitignore"), "w").write(
        "# Copied by tools/lift/lift_drawing_tests.py, not committed: Code New Roman's licence text is contradictory.\nfonts/\n")
    res_items = "\n".join(f'    <EmbeddedResource Include="TestData/{rel}" LogicalName="{logical}" />' for rel, logical in resources)
    open(os.path.join(dest, name + ".csproj"), "w").write(f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- System.Drawing.Common.Tests ({rt}, release/6.0), lifted by tools/lift/lift_drawing_tests.py, with test data
       from {ra} (release/6.0). MIT. Runs against the fork's System.Drawing.Common (SkiaGdiPlus). Generated; do not edit. -->
  <PropertyGroup>
{COMMON_PROPS}
    <AssemblyName>System.Drawing.Common.Tests</AssemblyName>
    <RootNamespace>System.Drawing.Tests</RootNamespace>
    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
    <DefineConstants>$(DefineConstants);NETCOREAPP</DefineConstants>
    <NoWarn>$(NoWarn);CS0618;CA1416;SYSLIB0011;SYSLIB0014;SYSLIB0051;xUnit1004;xUnit1013;xUnit1026;xUnit2000;xUnit2005;xUnit2009;xUnit2013;xUnit2017</NoWarn>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="**/*.cs" Exclude="bin/**;obj/**" />
{res_items}
    <!-- The test data packages' contentFiles land in the output directory (bitmaps/, fonts/, colorProfiles/, *.wav). -->
    <None Include="TestData/**" Link="%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/webForCore.System.Drawing.Common/webForCore.System.Drawing.Common.csproj" />
    <ProjectReference Include="../webForCore.Microsoft.DotNet.XUnitExtensions/webForCore.Microsoft.DotNet.XUnitExtensions.csproj" />
    <ProjectReference Include="../webForCore.Microsoft.DotNet.RemoteExecutor/webForCore.Microsoft.DotNet.RemoteExecutor.csproj" />
    <ProjectReference Include="../webForCore.TestUtilities/webForCore.TestUtilities.csproj" />
    <PackageReference Include="xunit" Version="{XUNIT_VERSION}" />
{XUNIT}
    <PackageReference Include="xunit.core" Version="{XUNIT_VERSION}" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.11.0" />
    <!-- 2.4.5, the adapter of xunit 2.4's time: 3.x runs the SkippedTestCase that ConditionalFact/Theory produce
         when a condition is false instead of skipping it. -->
    <PackageReference Include="xunit.runner.visualstudio" Version="2.4.5" />
  </ItemGroup>
</Project>
""")
    assert not lift.fixups, lift.fixups
    open(os.path.join(fork, "tools/lift/manifests", NAME + ".tsv"), "w").write(
        "# source repository and commit\tsource path\tdestination (relative to fork root)\n" + "\n".join(lift.manifest) + "\n")
    print(f"{len(lift.manifest)} files ({len(items)} test sources, {data} test data files)")


if __name__ == "__main__":
    main()
