#!/usr/bin/env python3
"""Print each failed test from a .trx with its message and the source line of the failing frame in the test files.

Usage: trx_failures.py results.trx <test-source-root> [name-filter ...]
"""
import os, re, sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx, root, filters = sys.argv[1], os.path.abspath(sys.argv[2]), sys.argv[3:]
r = ET.parse(trx).getroot()
classes = {u.get("id"): u.find("t:TestMethod", NS).get("className") for u in r.iterfind(".//t:UnitTest", NS)}
for x in r.iterfind(".//t:UnitTestResult", NS):
    name = classes.get(x.get("testId"), "").rsplit(".", 1)[-1] + "." + x.get("testName")
    if x.get("outcome") != "Failed" or (filters and not any(f in name for f in filters)):
        continue
    msg = (x.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=NS) or "").strip()
    st = x.findtext(".//t:ErrorInfo/t:StackTrace", default="", namespaces=NS) or ""
    print(f"■ {name}\n    {msg.replace(chr(13), '').replace(chr(10), ' ⏎ ')[:400]}")
    for m in re.finditer(r" in (" + re.escape(root) + r"/[^:]+):line (\d+)", st):
        path, line = m.group(1), int(m.group(2))
        src = open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
        print(f"    @ {os.path.relpath(path, root)}:{line}: {src[line - 1].strip()[:160]}")
        break
