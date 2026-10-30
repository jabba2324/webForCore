#!/usr/bin/env python3
"""Group MSBuild/Roslyn errors by project, error code and missing API.

Usage: group_errors.py build-logs/*.log   (or pipe a log on stdin)
Prints Markdown. Duplicate lines (MSBuild repeats errors) are counted once.
"""
import collections, re, sys

# The project suffix may carry "::TargetFramework=..." when the project multi-targets.
LINE = re.compile(r"^(?P<file>.+?)\((?P<line>\d+),(?P<col>\d+)\):\s+error\s+(?P<code>[A-Z]+\d+):\s+(?P<msg>.*?)(?:\s+\[(?P<proj>[^\]]+?\.csproj)(?:::[^\]]*)?\])?\s*$")
QUOTED = re.compile(r"'([^']+)'")
NS_MISSING = re.compile(r"namespace name '([^']+)' does not exist in the namespace '([^']+)'")
MEMBER_MISSING = re.compile(r"'([^']+)' does not contain a definition for '([^']+)'")


def symbol(code, msg):
    """Best-effort 'what API is missing' key for a diagnostic."""
    m = NS_MISSING.search(msg)
    if m:
        return f"{m.group(2)}.{m.group(1)}"
    m = MEMBER_MISSING.search(msg)
    if m:
        return f"{m.group(1)}.{m.group(2)}"
    if code in ("CS0246", "CS0103", "CS0117", "CS0234", "CS1061", "CS0012", "CS7069", "CS0122", "CS0535", "CS0115"):
        q = QUOTED.findall(msg)
        if q:
            return q[0]
    return None


def main(paths):
    texts = [open(p, encoding="utf-8", errors="replace").read() for p in paths] if paths else [sys.stdin.read()]
    seen, rows = set(), []
    for t in texts:
        for raw in t.splitlines():
            m = LINE.match(raw.strip())
            if not m:
                continue
            key = (m["file"], m["line"], m["col"], m["code"], m["msg"])
            if key in seen:
                continue
            seen.add(key)
            proj = m["proj"].rsplit("/", 1)[-1] if m["proj"] else "(unknown project)"
            rows.append((proj, m["code"], m["msg"], m["file"], symbol(m["code"], m["msg"])))
    by_proj = collections.defaultdict(list)
    for r in rows:
        by_proj[r[0]].append(r)
    print(f"# Compile errors: {len(rows)} unique across {len(by_proj)} project(s)\n")
    print("| Project | Errors | Distinct codes | Distinct missing APIs |\n| --- | --- | --- | --- |")
    for proj, rs in sorted(by_proj.items(), key=lambda kv: -len(kv[1])):
        print(f"| {proj} | {len(rs)} | {len({r[1] for r in rs})} | {len({r[4] for r in rs if r[4]})} |")
    all_syms = collections.Counter(r[4] for r in rows if r[4])
    print("\n## Most common missing APIs (all projects)\n\n| Count | Symbol | Projects |\n| --- | --- | --- |")
    for sym, n in all_syms.most_common(40):
        projs = sorted({r[0] for r in rows if r[4] == sym})
        print(f"| {n} | `{sym}` | {', '.join(p.replace('.csproj', '') for p in projs)} |")
    for proj, rs in sorted(by_proj.items(), key=lambda kv: -len(kv[1])):
        print(f"\n## {proj}\n")
        codes = collections.Counter(r[1] for r in rs)
        print("Codes: " + ", ".join(f"{c} x{n}" for c, n in codes.most_common(8)))
        files = collections.Counter(r[3].split("/src/")[-1] for r in rs)
        print("\nWorst files:\n")
        for f, n in files.most_common(8):
            print(f"- {n} `{f}`")
        syms = collections.Counter(r[4] for r in rs if r[4])
        if syms:
            print("\nTop missing APIs:\n")
            for s, n in syms.most_common(12):
                print(f"- {n} `{s}`")


if __name__ == "__main__":
    main(sys.argv[1:])
