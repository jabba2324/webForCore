#!/usr/bin/env python3
"""Lift System.Drawing.Common 6.0 (dotnet/runtime release/6.0, MIT) into the fork, with GDI+ on SkiaSharp.

System.Drawing.Common 6.0 is the last version with a Unix implementation; it calls the GDI+ flat API ("Gdip*"
functions) in libgdiplus, which was tried and rejected (LIFT.md). This lift keeps Microsoft's managed code - the
public API, argument validation, exceptions and the Unix code paths - and replaces the native library:
  - The compile list is the csproj's own for a Unix build ('$(TargetsAnyOS)' != 'true' plus '$(TargetsUnix)' == 'true');
    LibraryResolver no longer enforces System.Drawing.EnableUnixSupport (it still loads libcups for printing).
  - Every [DllImport(LibraryName)] extern in the Gdip class becomes a method with the same signature that calls
    SkiaGdiPlus, the managed GDI+ on SkiaSharp in tools/lift/additions/System.Drawing.Common/.
  - SkiaGdiPlus.Stubs.g.cs is generated: one stub per flat-API function the additions do not implement, returning
    GDI+'s NotImplemented status (Microsoft's code turns it into NotImplementedException).
  - SR.cs is generated from Resources/Strings.resx (dotnet/runtime generates it at build time).
Files are copied byte for byte, then the transforms below are applied.

Usage: lift_drawing.py <dotnet-runtime-6.0 checkout> <ForkRoot>
Writes tools/lift/manifests/System.Drawing.Common.tsv.
"""
import os, re, shutil, subprocess, sys
import xml.etree.ElementTree as ET

NAME = "System.Drawing.Common"
PROJECT = "webForCore.System.Drawing.Common"
EXCLUDE = set()

# (relative path, old, new, expected count). Text uses "\n"; files are matched in their own line endings.
FIXUPS = [
    # .NET Framework 4.8 behaviour: an unknown format name was left to TypeConverter.ConvertFrom (NotSupportedException).
    # The 4.8 System.Drawing source is not published; the evidence is Microsoft's own code that ran on it:
    # System.Web.Helpers (ConversionUtil.TryFromStringToImageFormat) catches only NotSupportedException, and WebImage's
    # tests (GetBytesThrowsOnIncorrectFormat, ...) expect it. .NET Core 3.0's rewrite throws FormatException.
    ("System/Drawing/ImageFormatConverter.cs",
     "            throw new FormatException(SR.Format(SR.ConvertInvalidPrimitive, strFormat, nameof(ImageFormat)));\n",
     "            // webForCore: as .NET Framework (NotSupportedException); .NET Core 3.0+ throws FormatException.\n"
     "            return base.ConvertFrom(context, culture, value);\n", 1),
    # libgdiplus needed this opt-in; the managed GDI+ does not. The resolver still loads libcups (printing).
    ("System/Drawing/LibraryResolver.cs",
     "            if (!LocalAppContextSwitches.EnableUnixSupport)\n                throw new PlatformNotSupportedException(SR.PlatformNotSupported_Unix);\n",
     "            // webForCore: System.Drawing.EnableUnixSupport guarded libgdiplus; GDI+ is managed here (SkiaGdiPlus).\n", 1),
    ("System/Drawing/GdiplusNative.Unix.cs",
     "                LibraryResolver.EnsureRegistered();\n",
     "                // webForCore: GDI+ is SkiaGdiPlus (managed, on SkiaSharp); no native library to resolve.\n", 1),
]

BUSY_EXEMPT = {"GdipReleaseDC", "GdipDeleteGraphics", "GdipGetDC"}

DLLIMPORT = re.compile(
    r"(?P<indent>[ \t]*)\[DllImport\(LibraryName[^\]]*\)\][ \t]*\r?\n"
    r"(?P<attrs>(?:[ \t]*\[return:[^\]]*\][ \t]*\r?\n)*)"
    r"[ \t]*(?P<access>internal|private|public) static extern (?P<unsafe>unsafe )?(?P<ret>[\w\.\*\[\]]+) (?P<name>\w+)\((?P<params>[^;]*?)\);",
    re.S)


def split_params(params):
    parts, depth, cur = [], 0, ""
    for ch in params:
        if ch in "([<":
            depth += 1
        elif ch in ")]>":
            depth -= 1
        if ch == "," and depth == 0:
            parts.append(cur)
            cur = ""
        else:
            cur += ch
    if cur.strip():
        parts.append(cur)
    return [p.strip() for p in parts]


def strip_attrs(param):
    return re.sub(r"^(\[[^\]]*\]\s*)+", "", param).strip()


def param_info(param):
    p = strip_attrs(param)
    tokens = p.split()
    mod = tokens[0] if tokens[0] in ("ref", "out", "in") else ""
    name = tokens[-1]
    ptype = " ".join(tokens[1:-1] if mod else tokens[:-1])
    return mod, ptype, name


def signature_key(name, params):
    return name + "(" + ",".join((m + " " + t).strip() for m, t, _ in map(param_info, split_params(params))) + ")"


def transform_dllimports(text, functions):
    nl = "\r\n" if "\r\n" in text else "\n"

    def repl(m):
        params = re.sub(r"\s+", " ", m.group("params")).strip()
        infos = [param_info(p) for p in split_params(params)]
        args = ", ".join((mod + " " + n).strip() for mod, _, n in infos)
        functions.append((m.group("ret"), m.group("name"), params))
        ind = m.group("indent")
        call = f"SkiaGdiPlus.{m.group('name')}({args})"
        body = f"=> {call};"
        # GDI+ answers ObjectBusy for any call on a Graphics whose HDC is out (GetHdc), except releasing it.
        if (infos and infos[0][1] == "HandleRef" and infos[0][2] == "graphics" and m.group("ret") == "int"
                and m.group("name") not in BUSY_EXEMPT):
            outs = "".join(f" {n} = default;" for mod, _, n in infos if mod == "out")
            body = f"=> SkiaGdiPlus.GraphicsBusy(graphics) ? SkiaGdiPlus.ObjectBusy : {call};"
            if outs:
                body = f"{{ if (SkiaGdiPlus.GraphicsBusy(graphics)) {{{outs} return SkiaGdiPlus.ObjectBusy; }} return {call}; }}"
        return (f"{ind}// webForCore: was [DllImport(LibraryName)] (libgdiplus); GDI+ on SkiaSharp (SkiaGdiPlus).{nl}"
                f"{ind}{m.group('access')} static {m.group('unsafe') or ''}{m.group('ret')} {m.group('name')}({params}) {body}")
    return DLLIMPORT.sub(repl, text)


def implemented(additions):
    keys = set()
    for root, _, files in os.walk(additions):
        for f in files:
            if not f.endswith(".cs"):
                continue
            text = open(os.path.join(root, f), encoding="utf-8").read()
            for m in re.finditer(r"internal static [\w\.\*\[\]]+ (Gdip\w+|Gdiplus\w+)\(([^)]*)\)", text):
                keys.add(signature_key(m.group(1), m.group(2)))
    return keys


def stubs(functions, done):
    out = ["// <auto-generated> tools/lift/lift_drawing.py: the GDI+ flat API functions SkiaGdiPlus does not implement yet.",
           "// Each returns GDI+'s NotImplemented status (6), which System.Drawing turns into NotImplementedException.",
           "// </auto-generated>",
           "#nullable disable",
           "using System.Drawing.Drawing2D;", "using System.Drawing.Imaging;", "using System.Drawing.Text;",
           "using System.Runtime.InteropServices;", "",
           "namespace System.Drawing", "{", "internal static partial class SafeNativeMethods", "{",
           "    internal static unsafe partial class SkiaGdiPlus", "    {"]
    seen = set()
    count = 0
    for ret, name, params in functions:
        key = signature_key(name, params)
        if key in done or key in seen:
            continue
        seen.add(key)
        count += 1
        outs = [n for mod, _, n in map(param_info, split_params(params)) if mod == "out"]
        body = "".join(f" {n} = default;" for n in outs)
        if ret == "void":
            out.append(f"        internal static void {name}({params}) {{{body} }}")
        elif ret == "int":
            out.append(f"        internal static int {name}({params}) {{{body} return NotImplemented; }}")
        else:
            out.append(f"        internal static {ret} {name}({params}) {{{body} return default; }}")
    out += ["    }", "}", "}", ""]
    return "\n".join(out), count


def sr_class(resx):
    names = [d.get("name") for d in ET.parse(resx).getroot().findall("data")]
    lines = ["// <auto-generated> tools/lift/lift_drawing.py, from Resources/Strings.resx (dotnet/runtime generates it at build",
             "// time). </auto-generated>",
             "using System.Resources;", "",
             "namespace System", "{", "    internal static partial class SR", "    {",
             "        private static ResourceManager s_resourceManager;",
             "        internal static ResourceManager ResourceManager => s_resourceManager ??= new ResourceManager(\"System.Drawing.Common.Strings\", typeof(SR).Assembly);",
             "        internal static string GetResourceString(string key) => ResourceManager.GetString(key) ?? key;",
             "        internal static string Format(string format, object arg0) => string.Format(format, arg0);",
             "        internal static string Format(string format, object arg0, object arg1) => string.Format(format, arg0, arg1);",
             "        internal static string Format(string format, object arg0, object arg1, object arg2) => string.Format(format, arg0, arg1, arg2);",
             "        internal static string Format(string format, params object[] args) => string.Format(format, args);"]
    for n in names:
        lines.append(f"        internal static string @{n} => GetResourceString(\"{n}\");")
    lines += ["    }", "}", ""]
    return "\n".join(lines)


def main():
    runtime, fork = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
    src = os.path.join(runtime, "src/libraries/System.Drawing.Common/src")
    common = os.path.join(runtime, "src/libraries/Common/src")
    dest = os.path.join(fork, "src", PROJECT)
    additions = os.path.join(fork, "tools/lift/additions", NAME)
    commit = subprocess.check_output(["git", "-C", runtime, "rev-parse", "--short", "HEAD"], text=True).strip()

    proj = ET.parse(os.path.join(src, "System.Drawing.Common.csproj")).getroot()
    compile_items, resources = [], []
    for group in proj.findall("ItemGroup"):
        cond = group.get("Condition", "")
        if cond not in ("'$(TargetsAnyOS)' != 'true'", "'$(TargetsUnix)' == 'true'"):
            continue
        for item in group:
            inc = item.get("Include").replace("\\", "/")
            if item.tag == "Compile":
                compile_items.append(inc)
            elif item.tag == "EmbeddedResource":
                resources.append((inc, item.findtext("LogicalName") or item.get("LogicalName")))

    for d in ("System", "Common", "Resources", "Interop"):
        shutil.rmtree(os.path.join(dest, d), ignore_errors=True)
    manifest = [f"# dotnet/runtime {commit} (release/6.0): source path (relative to repo root)\tdestination (relative to fork root)"]
    functions = []
    fixups_left = list(FIXUPS)

    def copy(source_abs, rel_dest):
        target = os.path.join(dest, rel_dest)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        shutil.copyfile(source_abs, target)
        manifest.append(f"{os.path.relpath(source_abs, runtime)}\t{os.path.relpath(target, fork)}")
        return target

    for inc in compile_items:
        if inc in EXCLUDE:
            continue
        if inc.startswith("$(CommonPath)"):
            rel = inc[len("$(CommonPath)"):]
            target = copy(os.path.join(common, rel), os.path.join("Common", rel))
            rel_src = None
        else:
            target = copy(os.path.join(src, inc), inc)
            rel_src = inc
        raw = open(target, "rb").read()
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        crlf = "\r\n" in text
        for fix in [f for f in fixups_left if f[0] == rel_src]:
            old, new, n = fix[1], fix[2], fix[3]
            if crlf:
                old, new = old.replace("\n", "\r\n"), new.replace("\n", "\r\n")
            assert text.count(old) == n, (rel_src, old, text.count(old))
            text = text.replace(old, new)
            fixups_left.remove(fix)
            print(f"   fixup {rel_src}")
        before = len(functions)
        # Only the GDI+ imports (class Gdip); LibcupsNative has its own LibraryName (libcups, printing).
        if "partial class Gdip" in text:
            text = transform_dllimports(text, functions)
        if len(functions) > before:
            print(f"   {len(functions) - before} DllImports -> SkiaGdiPlus in {rel_src or rel}")
        open(target, "wb").write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
    assert not fixups_left, fixups_left

    for inc, logical in resources:
        copy(os.path.join(src, inc), inc)
    copy(os.path.join(src, "Resources/Strings.resx"), "Resources/Strings.resx")
    open(os.path.join(dest, "SR.g.cs"), "w").write(sr_class(os.path.join(src, "Resources/Strings.resx")))

    # The engine (webForCore's own code) and the stubs for what it does not implement yet.
    engine = os.path.join(dest, "SkiaGdiPlus")
    shutil.rmtree(engine, ignore_errors=True)
    shutil.copytree(additions, engine)
    text, count = stubs(functions, implemented(additions))
    open(os.path.join(engine, "SkiaGdiPlus.Stubs.g.cs"), "w").write(text)
    total = len({signature_key(n, p) for _, n, p in functions})
    print(f"{total} GDI+ flat API functions; {total - count} implemented, {count} stubs")

    res_items = "\n".join(
        f'    <EmbeddedResource Include="{inc.replace("/", chr(92))}"' + (f' LogicalName="{logical}"' if logical else "") + " />"
        for inc, logical in resources)
    csproj = f"""<Project Sdk="Microsoft.NET.Sdk">

  <!-- System.Drawing.Common 6.0 (dotnet/runtime {commit}, release/6.0), lifted by tools/lift/lift_drawing.py: Microsoft's
       managed code for a Unix build, with the GDI+ flat API implemented in managed code on SkiaSharp (SkiaGdiPlus/)
       instead of libgdiplus. Generated; do not edit (change the lift script or tools/lift/additions/{NAME}/). -->
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AssemblyName>System.Drawing.Common</AssemblyName>
    <RootNamespace>System.Drawing</RootNamespace>
    <AssemblyVersion>11.0.0.0</AssemblyVersion>
    <FileVersion>11.0.0.0</FileVersion>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DefineConstants>$(DefineConstants);DRAWING_NAMESPACE</DefineConstants>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
    <GenerateResourceUsePreserializedResources>false</GenerateResourceUsePreserializedResources>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS0618;CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625;CS8765;CS8767;CA1416;SYSLIB0003;SYSLIB0011;SYSLIB0051;CS0649;CS0169;CS0414</NoWarn>
    <IsPackable>false</IsPackable>
    <SignAssembly>True</SignAssembly>
    <AssemblyOriginatorKeyFile>..\\SigningKey\\WebFormsForCore.snk</AssemblyOriginatorKeyFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="**\\*.cs" Exclude="bin\\**;obj\\**" />
    <EmbeddedResource Include="Resources\\Strings.resx" LogicalName="System.Drawing.Common.Strings.resources" />
{res_items}
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="SkiaSharp" Version="3.119.0" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="3.119.0" />
  </ItemGroup>

  <Import Project="..\\..\\lib\\WebFormsForCore.Build\\netstandard2.0\\FakeStrongName.targets" />

  <!-- System.Drawing.Common's identity (the shared framework's System.Drawing facade forwards to it). -->
  <PropertyGroup>
    <StrongNamePublicKey>00240000048000009400000006020000002400005253413100040000010001004b86c4cb78549b34bab61a3b1800e23bfeb5b3ec390074041536a7e3cbd97f5f04cf0f857155a8928eaa29ebfd11cfbbad3ba70efea7bda3226c6a8d370a4cd303f714486b6ebc225985a638471e6ef571cc92a4613c00b8fa65d61ccee0cbe5f36330c9a01f4183559f1bef24cc2917c6d913e3a541333a1d05d9bed22b38cb</StrongNamePublicKey>
    <StrongNamePublicKeyToken>cc7b13ffcd2ddd51</StrongNamePublicKeyToken>
  </PropertyGroup>
</Project>
"""
    open(os.path.join(dest, PROJECT + ".csproj"), "w").write(csproj)
    os.makedirs(os.path.join(fork, "tools/lift/manifests"), exist_ok=True)
    open(os.path.join(fork, "tools/lift/manifests", NAME + ".tsv"), "w").write("\n".join(manifest) + "\n")
    print(f"{len(manifest) - 1} files")


if __name__ == "__main__":
    main()
