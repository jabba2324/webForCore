#!/usr/bin/env python3
"""Replaces the ResX classes in src/WebFormsForCore.Web/WebFormsForCore/System.Resources (ResXResourceReader and its
helpers, which System.Web's ResXBuildProvider uses for App_GlobalResources and App_LocalResources), which
WebFormsForCore decompiled from System.Windows.Forms.dll, with their MIT-licensed originals from dotnet/winforms
(release/8.0), so that no decompiled code remains (docs/LICENSING.md).

Usage: lift_resx.py <dotnet/winforms checkout> [ForkRoot]

The files are copied to WebFormsForCore/System.Resources/winforms/, changed only by FIXUPS: winforms' strings come from
ResXSR (generated from the strings these files use, taken from winforms' SR.resx), and serialized values are read
and written with webForCore's BinaryFormatter port, as .NET's BinaryFormatter throws.
"""
import os
import re
import subprocess
import sys

COMMIT = "8266870"  # dotnet/winforms release/8.0
PROJECT = "src/WebFormsForCore.Web/WebFormsForCore/System.Resources"
DEST = "winforms"
SOURCE = "src/System.Windows.Forms/src/System/Resources"

REMOVE = ["AssemblyNamesTypeResolutionService.cs", "ClientUtils.cs", "DataNodeInfo.cs", "IAliasResolver.cs",
          "MultitargetUtil.cs", "ResXDataNode.cs", "ResXFileRef.cs", "ResXNullRef.cs", "ResXResourceReader.cs",
          "ResXResourceSet.cs", "ResXResourceWriter.cs", "ResXSerializationBinder.cs"]

COPY = [f"{SOURCE}/{name}.cs" for name in (
    "AssemblyNamesTypeResolutionService", "DataNodeInfo", "IAliasResolver", "ResXDataNode", "ResxFileRef.Converter",
    "ResXFileRef", "ResXNullRef", "ResXResourceReader", "ResXResourceReader.ReaderAliasResolver", "ResXResourceSet",
    "ResXResourceWriter", "ResXSerializationBinder")] + ["src/System.Windows.Forms/src/misc/MultitargetUtil.cs"]

FORMATTER = ("using IFormatter = WebFormsForCore.Serialization.Formatters.IFormatter;\n"
             "using BinaryFormatter = WebFormsForCore.Serialization.Formatters.Binary.BinaryFormatter; // webForCore: the port; .NET's throws\n")

# (old, new, count), or ("between", start, end, new): replace from start up to and including end.
FIXUPS = {
    # System.Web references System.Drawing.Common through the extern alias Common.
    f"{SOURCE}/ResxFileRef.Converter.cs": [
        ("typeof(Bitmap)", "typeof(Common::System.Drawing.Bitmap)", 1),
        ("                Icon ico = new Icon(memoryStream);", "                Common::System.Drawing.Icon ico = new Common::System.Drawing.Icon(memoryStream);", 1),
    ],
    # winforms' TraceSwitch.TraceVerbose(message) extension.
    f"{SOURCE}/ResXResourceWriter.cs": [
        ("regex", r"s_resValueProviderSwitch\.TraceVerbose\((.*)\);", r"Debug.WriteLineIf(s_resValueProviderSwitch.TraceVerbose, \1);", 3),
    ],
    f"{SOURCE}/ResXDataNode.cs": [
        ("using System.Runtime.Serialization.Formatters.Binary;\n", FORMATTER, 1),
        ("using System.Windows.Forms.BinaryFormat;\n", "", 1),
        # winforms 8 writes and reads the common framework types with its own NRBF reader/writer first; the formatter
        # writes the same format.
        ("between", "                bool success = false;\n", "                    binaryFormatter.Serialize(stream, value);\n                }\n",
         "                binaryFormatter.Serialize(stream, value); // webForCore: winforms' BinaryFormatWriter fast path left out\n"),
        ("between", "        try\n        {\n            BinaryFormattedObject format", "        stream.Position = 0;\n",
         "        // webForCore: winforms' BinaryFormattedObject fast path left out\n"),
    ],
}

STRINGS = "src/System.Windows.Forms/src/Resources/SR.resx"


def git_show(checkout, path):
    return subprocess.run(["git", "-C", checkout, "show", f"{COMMIT}:{path}"], capture_output=True, check=True).stdout


def main():
    wf = sys.argv[1]
    fork = sys.argv[2] if len(sys.argv) > 2 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    project = os.path.join(fork, PROJECT)
    for rel in REMOVE:
        if os.path.exists(os.path.join(project, rel)):
            os.remove(os.path.join(project, rel))
    manifest = [f"# dotnet/winforms {COMMIT}: source path (relative to repo root)\tdestination (relative to fork root)"]
    used = set()
    for src in COPY:
        raw = git_show(wf, src)
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        for fix in FIXUPS.get(src, []):
            if fix[0] == "regex":
                _, pattern, repl, count = fix
                text, n = re.subn(pattern, repl, text)
                if n != count:
                    sys.exit(f"{src}: regex fixup matched {n} times, expected {count}: {pattern!r}")
                continue
            if fix[0] == "between":
                _, start, end, new = fix
                i = text.find(start)
                j = text.find(end, i)
                if i < 0 or j < 0 or text.count(start) != 1:
                    sys.exit(f"{src}: fixup between {start!r} and {end!r} did not match")
                text = text[:i] + new + text[j + len(end):]
            else:
                old, new, count = fix
                if text.count(old) != count:
                    sys.exit(f"{src}: fixup matched {text.count(old)} times, expected {count}: {old!r}")
                text = text.replace(old, new)
        text = text.replace("using System.Windows.Forms;\n", "")
        used |= set(re.findall(r"\bSR\.(\w+)", text))
        text = re.sub(r"\bSR\.", "ResXSR.", text)
        # winforms builds with nullable reference types on and project-wide global usings
        text = ("#nullable enable\n// webForCore: winforms' global usings\nusing System.Collections.Generic;\nusing System.Diagnostics;\n"
                "using System.Diagnostics.CodeAnalysis;\nusing System.IO;\nusing System.Linq;\n") + text
        if "Common::" in text:
            text = "extern alias Common;\n" + text
        if crlf:
            text = text.replace("\n", "\r\n")
        dest = os.path.join(project, DEST, os.path.basename(src))
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        with open(dest, "wb") as f:
            f.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
        manifest.append(f"{src}\t{PROJECT}/{DEST}/{os.path.basename(src)}")
    # The strings these files use, from winforms' SR.resx, and ResXSR to read them.
    resx = git_show(wf, STRINGS).decode("utf-8-sig")
    entries = []
    for name in sorted(used):
        m = re.search(r'<data name="' + re.escape(name) + r'"[^>]*>.*?</data>', resx, re.S)
        if not m:
            sys.exit(f"string {name} not in winforms' SR.resx")
        entries.append("  " + m.group(0))
    head = ('<?xml version="1.0" encoding="utf-8"?>\n<!-- webForCore: the strings of dotnet/winforms\' SR.resx (MIT) that its'
            ' ResX classes use; written by tools/lift/lift_resx.py. -->\n<root>\n'
            '  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>\n'
            '  <resheader name="version"><value>2.0</value></resheader>\n'
            '  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>\n'
            '  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>\n')
    out = os.path.join(project, DEST, "ResXSR.resx")
    with open(out, "w", encoding="utf-8") as f:
        f.write(head + "\n".join(entries) + "\n</root>\n")
    subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "gen_sr.py"), out,
                    os.path.join(project, DEST, "ResXSR.g.cs"), "--namespace", "System.Resources", "--class", "ResXSR",
                    "--resource", "System.Resources.ResXSR", "--properties"], check=True)
    with open(os.path.join(fork, "tools", "lift", "manifests", "ResX.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest) + "\n")
    print(f"{len(REMOVE)} decompiled files removed, {len(COPY)} winforms files copied, {len(entries)} strings")


if __name__ == "__main__":
    main()
