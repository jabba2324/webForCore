#!/usr/bin/env python3
"""Replaces two API reconstructions in src/WebFormsForCore.Web/WebFormsForCore with MIT-licensed originals
(docs/LICENSING.md):
- System.Resources.Tools.StronglyTypedResourceBuilder (System.Design), which System.Web's resource build providers use
  for App_GlobalResources, from Mono (mcs/class/System.Design, MIT, Copyright (C) 2007 Novell, Inc.);
- System.ComponentModel.DataAnnotations.BindableTypeAttribute from the .NET Framework Reference Source.

Usage: lift_strb.py <mono checkout> <referencesource checkout> [ForkRoot]
"""
import os
import subprocess
import sys

PROJECT = "src/WebFormsForCore.Web/WebFormsForCore"

# (repository name, checkout argument index, commit, source, destination relative to PROJECT)
COPY = [
    ("mono/mono", 1, "0f53e9e151d92944cacab3e24ac359410c606df6",
     "mcs/class/System.Design/System.Resources.Tools/StronglyTypedResourceBuilder.cs", "mono/StronglyTypedResourceBuilder.cs"),
    ("microsoft/referencesource", 2, "ec9fa9ae770d522a5b5f0607898044b7478574a3",
     "System.ComponentModel.DataAnnotations/DataAnnotations/BindableTypeAttribute.cs", "ReferenceSource/BindableTypeAttribute.cs"),
]

REMOVE = ["System.Resources/StronglyTypedResourceBuilder.cs", "System.Resources/ITargetAwareCodeDomProvider.cs",
          "BindableTypeAttribute.cs"]

FIXUPS = {
    # System.Web passes the fork's CodeDomProvider (WebFormsForCore.CodeDom.Compiler), which has the same members.
    "mcs/class/System.Design/System.Resources.Tools/StronglyTypedResourceBuilder.cs": [
        ("using System.CodeDom.Compiler;\n",
         "using System.CodeDom.Compiler;\nusing CodeDomProvider = WebFormsForCore.CodeDom.Compiler.CodeDomProvider; // webForCore: System.Web's provider type\n", 1),
    ],
}


def main():
    fork = sys.argv[3] if len(sys.argv) > 3 else os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    project = os.path.join(fork, PROJECT)
    for rel in REMOVE:
        if os.path.exists(os.path.join(project, rel)):
            os.remove(os.path.join(project, rel))
    manifest = ["# source repository and commit\tsource path\tdestination (relative to fork root)"]
    for repo, arg, commit, src, dest in COPY:
        raw = subprocess.run(["git", "-C", sys.argv[arg], "show", f"{commit}:{src}"], capture_output=True, check=True).stdout
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        crlf = "\r\n" in text
        text = text.replace("\r\n", "\n")
        for old, new, count in FIXUPS.get(src, []):
            if text.count(old) != count:
                sys.exit(f"{src}: fixup matched {text.count(old)} times, expected {count}: {old!r}")
            text = text.replace(old, new)
        if crlf:
            text = text.replace("\n", "\r\n")
        path = os.path.join(project, dest)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write((b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8"))
        manifest.append(f"{repo} {commit[:7]}\t{src}\t{PROJECT}/{dest}")
    with open(os.path.join(fork, "tools", "lift", "manifests", "System.Web.Additions.tsv"), "w", encoding="utf-8") as f:
        f.write("\n".join(manifest) + "\n")
    print(f"{len(REMOVE)} reconstructed files removed, {len(COPY)} originals copied")


if __name__ == "__main__":
    main()
