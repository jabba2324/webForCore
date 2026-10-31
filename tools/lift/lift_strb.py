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
    # System.Data's typed DataSet generator, which .NET dropped; XsdBuildProvider (.xsd files in App_Code) builds on it
    # (TypedDataSets/TypedDataSetGenerator.cs).
    ("microsoft/referencesource", 2, "ec9fa9ae770d522a5b5f0607898044b7478574a3",
     "System.Data/System/Data/CodeGen/datacache.cs", "ReferenceSource/datacache.cs"),
]

REMOVE = ["System.Resources/StronglyTypedResourceBuilder.cs", "System.Resources/ITargetAwareCodeDomProvider.cs",
          "BindableTypeAttribute.cs"]

FIXUPS = {
    # System.Web passes the fork's CodeDomProvider (WebFormsForCore.CodeDom.Compiler), which has the same members.
    "mcs/class/System.Design/System.Resources.Tools/StronglyTypedResourceBuilder.cs": [
        ("using System.CodeDom.Compiler;\n",
         "using System.CodeDom.Compiler;\nusing CodeDomProvider = WebFormsForCore.CodeDom.Compiler.CodeDomProvider; // webForCore: System.Web's provider type\n", 1),
    ],
    # Compiled into System.Web, where it is internal; System.Data internals it used are replaced by public members
    # (TypedDataSets/CodeGenSupport.cs); its messages come from System.Data's resources (system.data.txt).
    "System.Data/System/Data/CodeGen/datacache.cs": [
        ("    public class TypedDataSetGenerator {",
         "    // webForCore: internal, for XsdBuildProvider (TypedDataSets/TypedDataSetGenerator.cs); System.Data internals replaced\n"
         "    // by public members (TypedDataSets/CodeGenSupport.cs).\n"
         "    internal class TypedDataSetGenerator {", 1),
        # HostProtection has no effect on .NET; Obsolete pointed to System.Design's generator.
        ('    [\n    System.Security.Permissions.HostProtectionAttribute(SharedState=true, Synchronization=true),\n'
         '    Obsolete("TypedDataSetGenerator class will be removed in a future release. Please use System.Data.Design.TypedDataSetGenerator in System.Design.dll.")\n'
         '    ]\n', "", 1),
        ("Res.GetString(Res.CodeGen_", "CodeGenSupport.GetString(CodeGenSupport.CodeGen_", 5),
        ("Str(Res.GetString(arg))", "Str(CodeGenSupport.GetString(arg))", 1),
        ("table.DataSet.Tables.InternalIndexOf(table.TableName) == -3", "CodeGenSupport.HasNamesake(table)", 1),
        ("Common.ADP.IsEmpty(", "isEmpty(", 2),
        ("column._columnUri != null", "CodeGenSupport.HasOwnNamespace(column)", 1),
        ("table.ShouldSerializeCaseSensitive()", "CodeGenSupport.HasOwnCaseSensitive(table)", 1),
        ("table.ShouldSerializeLocale()", "CodeGenSupport.HasOwnLocale(table)", 1),
        ("table.tableNamespace != null", "CodeGenSupport.HasOwnNamespace(table)", 1),
        ("table.displayExpression != null", "table.DisplayExpression.Length != 0", 1),
        ("Str(table.DisplayExpressionInternal)", "Str(table.DisplayExpression)", 1),
        ("ChildColumnsReference", "ChildColumns", 2),
        ("ParentColumnsReference", "ParentColumns", 2),
        ("RelatedColumnsReference", "RelatedColumns", 1),
        (").ColumnsReference", ").Columns", 1),
        ("uc.ColumnsReference", "uc.Columns", 1),
        ("index[i].FindParentRelation()", "CodeGenSupport.FindParentRelation(index[i])", 2),
        ("col.ConvertXmlToObject(nullValue)", "CodeGenSupport.ConvertXmlToObject(col, nullValue)", 1),
        # .NET has no TypedDataSetGeneratorException.
        ("throw new TypedDataSetGeneratorException(errorList);", "throw CodeGenSupport.GeneratorException(errorList);", 1),
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
