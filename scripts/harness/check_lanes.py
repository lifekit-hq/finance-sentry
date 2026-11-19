#!/usr/bin/env python3
"""Checks the lane table in docs/harness/gate-contract.md against the tracked files.

Every glob must match at least one tracked file, and no tracked file may match globs of two lanes.
Usage: check_lanes.py [path]. Exits 1 and names each problem; reads files only.
"""
import os
import re
import subprocess
import sys

REPO = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT = os.path.join(REPO, "docs", "harness", "gate-contract.md")
ROW = re.compile(r"^\|\s*(full|light)\s*\|[^|]*\|\s*`([^`]+)`\s*\|\s*$", re.M)


def parse(text):
    return [(lane, glob) for lane, glob in ROW.findall(text)]


def regex(glob):
    out, i = "", 0
    while i < len(glob):
        if glob.startswith("**", i):
            out += ".*"
            i += 2
        elif glob[i] == "*":
            out += "[^/]*"
            i += 1
        else:
            out += re.escape(glob[i])
            i += 1
    return re.compile("^" + out + "$")


def problems(rows, files):
    found = []
    if not rows:
        return ["no lane rows found"]
    if not {lane for lane, _ in rows} == {"full", "light"}:
        found.append("both lanes 'full' and 'light' need at least one row")
    owners = {}
    for lane, glob in rows:
        rx = regex(glob)
        hits = [f for f in files if rx.match(f)]
        if not hits:
            found.append("glob '%s' (%s) matches no file" % (glob, lane))
        for f in hits:
            owners.setdefault(f, set()).add(lane)
    for f in sorted(owners):
        if len(owners[f]) > 1:
            found.append("path '%s' is in two lanes" % f)
    return found


def tracked():
    out = subprocess.run(["git", "-C", REPO, "ls-files"], capture_output=True, text=True, check=True).stdout
    return out.splitlines()


def main(argv):
    path = argv[1] if len(argv) > 1 else DEFAULT
    with open(path, encoding="utf-8") as fh:
        found = problems(parse(fh.read()), tracked())
    for item in found:
        print("lanes: " + item, file=sys.stderr)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
