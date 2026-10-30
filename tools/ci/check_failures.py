#!/usr/bin/env python3
"""Fails when a .trx has a failed test that is not in the suite's known-failures list.

Usage: check_failures.py results.trx [known-failures.txt]
Known failures are one "Class.Method(arguments)" per line, as this script prints them; '#' starts a comment.
Tests on the list that now pass are reported, so the list can shrink.
"""
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def results(trx):
    root = ET.parse(trx).getroot()
    classes = {u.get("id"): u.find("t:TestMethod", NS).get("className") for u in root.iterfind(".//t:UnitTest", NS)}
    for r in root.iterfind(".//t:UnitTestResult", NS):
        name = r.get("testName")
        cls = classes.get(r.get("testId"), "")
        if not name.startswith(cls):
            name = cls + "." + name
        yield name, r.get("outcome")


def main():
    trx = sys.argv[1]
    known = set()
    if len(sys.argv) > 2:
        for line in open(sys.argv[2], encoding="utf-8"):
            line = line.split("#", 1)[0].strip()
            if line:
                known.add(line)
    pairs = list(results(trx))  # theory cases can share a display name: any failing case counts as a failure
    failed = {n for n, o in pairs if o == "Failed"}
    passed = {n for n, o in pairs if o == "Passed"} - failed
    new = sorted(failed - known)
    fixed = sorted(n for n in known if n in passed)
    print(f"{trx}: {len(pairs)} results, {len(failed)} failed ({len(failed & known)} known, {len(new)} new)")
    for n in fixed:
        print(f"  now passes (remove from the known list): {n}")
    for n in new:
        print(f"::error::{n} failed")
    return 1 if new else 0


if __name__ == "__main__":
    sys.exit(main())
