#!/usr/bin/env python3
"""Checks docs/harness/gate-contract.md records the freeze date, the version and a model line for every gate step.

Usage: check_gate_contract.py [path]. Exits 1 and names each missing item; reads the file only.
"""
import os
import re
import sys

STEPS = ("intent", "rebase", "review", "test", "document", "lint", "push", "pr", "ci")
DEFAULT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "docs", "harness", "gate-contract.md")


def problems(text):
    found = []
    if not re.search(r"^- Frozen: \d{4}-\d{2}-\d{2}\s*$", text, re.M):
        found.append("missing freeze date line '- Frozen: YYYY-MM-DD'")
    if not re.search(r"^- no-mistakes version: v\d+\.\d+\.\d+\s*$", text, re.M):
        found.append("missing version line '- no-mistakes version: vX.Y.Z'")
    for step in STEPS:
        if not re.search(r"^\|\s*%s\s*\|\s*model:\s*\S+" % re.escape(step), text, re.M):
            found.append("missing model line for step '%s'" % step)
    return found


def main(argv):
    path = argv[1] if len(argv) > 1 else DEFAULT
    with open(path, encoding="utf-8") as fh:
        found = problems(fh.read())
    for item in found:
        print("gate-contract: " + item, file=sys.stderr)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
