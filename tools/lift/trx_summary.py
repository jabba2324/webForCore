#!/usr/bin/env python3
"""Summarise VSTest .trx files: totals, then failures grouped by test class and by message.

Usage: trx_summary.py results/*.trx   Prints Markdown.
"""
import collections, re, sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def norm(msg):
    """Collapse a failure message to a groupable key: first line, numbers and quoted values blanked."""
    first = (msg or "").strip().splitlines()[0] if msg else "(no message)"
    first = re.sub(r"'[^']*'|\"[^\"]*\"", "'…'", first)
    first = re.sub(r"\d+", "N", first)
    return first[:150]


for path in sys.argv[1:]:
    root = ET.parse(path).getroot()
    tests = {u.get("id"): u for u in root.iterfind(".//t:UnitTest", NS)}
    counters = root.find(".//t:ResultSummary/t:Counters", NS)
    total, passed, failed = (counters.get(k) for k in ("total", "passed", "failed"))
    skipped = int(total) - int(passed) - int(failed)
    name = path.rsplit("/", 1)[-1].replace(".trx", "")
    print(f"## {name}\n\nTotal {total}, passed {passed}, failed {failed}, not run {skipped} "
          f"(pass rate {100 * int(passed) / max(1, int(total)):.1f}%)\n")
    by_class, by_msg = collections.Counter(), collections.Counter()
    examples = {}
    for r in root.iterfind(".//t:UnitTestResult", NS):
        if r.get("outcome") != "Failed":
            continue
        tm = tests[r.get("testId")].find("t:TestMethod", NS)
        cls = tm.get("className").rsplit(".", 1)[-1]
        msg = r.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=NS)
        key = norm(msg)
        by_class[cls] += 1
        by_msg[key] += 1
        examples.setdefault(key, f"{cls}.{tm.get('name')}")
    if by_class:
        print("| Failures | Test class |\n| --- | --- |")
        for c, n in by_class.most_common(15):
            print(f"| {n} | `{c}` |")
        print("\n| Failures | Message (normalised) | Example |\n| --- | --- | --- |")
        for m, n in by_msg.most_common(15):
            print(f"| {n} | {m.replace('|', '/')} | `{examples[m]}` |")
    print()
