#!/usr/bin/env python3
"""Summarises a Cobertura file by assembly: line coverage of webForCore's production code.

Usage: coverage_summary.py merged.cobertura.xml   Prints Markdown. Test assemblies, test helpers, samples and
third-party packages are left out; assemblies are grouped by where their source comes from.
"""
import os
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def origin(filename):
    rel = os.path.relpath(filename, ROOT) if filename and os.path.isabs(filename) else (filename or "")
    parts = rel.replace("\\", "/").split("/")
    if len(parts) > 1 and parts[0] == "src":
        return parts[1]
    return None


def main():
    root = ET.parse(sys.argv[1]).getroot()
    stats = {}
    for package in root.iter("package"):
        for cls in package.iter("class"):
            project = origin(cls.get("filename"))
            if project is None or "TestApp" in project or project.endswith("Testing"):
                continue
            s = stats.setdefault((package.get("name"), project), {})
            for line in cls.iter("line"):
                key = (cls.get("filename"), line.get("number"))
                s[key] = s.get(key, False) or int(line.get("hits", "0")) > 0
    rows, tl, tc = [], 0, 0
    for (name, project), s in sorted(stats.items(), key=lambda kv: kv[0][1].lower()):
        lines = len(s)
        covered = sum(s.values())
        if lines == 0:
            continue
        tl, tc = tl + lines, tc + covered
        kind = "lifted" if project.startswith("webForCore.") else "port"
        rows.append(f"| {name} | {kind} | {lines:,} | {covered:,} | {100 * covered / lines:.1f}% |")
    print("| Assembly | Source | Coverable lines | Covered | Line coverage |")
    print("| --- | --- | ---: | ---: | ---: |")
    print("\n".join(rows))
    print(f"| **Total** | | **{tl:,}** | **{tc:,}** | **{100 * tc / tl:.1f}%** |")


if __name__ == "__main__":
    main()
