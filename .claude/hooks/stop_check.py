#!/usr/bin/env python3
"""Stop: refuse the end of the turn while the diff-scoped build/tests are red.

Same split as `.no-mistakes.yaml` `commands.test`, diffed against the merge-base with origin/main
(working-tree changes and untracked files included):
  backend/** or Directory.*.props -> backend half (restore, build only: measured 12 min on the PC with tests, over the 5 min budget; the non-integration test projects
                                    run in .husky/pre-push, see scripts/backend-tests.sh)
  frontend/**                     -> frontend half (lint, build, test:ci)
  anything else (not docs)        -> both halves
  empty diff / docs only          -> nothing
Red -> exit 2 with the first error lines on stderr. Green -> exit 0. `stop_hook_active` -> exit 0,
so the hook cannot loop. Read-only: it never edits a file (build output lands in gitignored dirs).
Test overrides: FS_STOP_ROOT, FS_STOP_BACKEND_CMD, FS_STOP_FRONTEND_CMD, FS_HOOK_LOG.
"""
import os
import re
import subprocess
import sys
import time

import hooklib

ROOT = os.environ.get("FS_STOP_ROOT") or hooklib.ROOT
SUMMARY_LINES = 20
HALF_TIMEOUT = 1500
ERROR_RE = re.compile(r"error|fail|✘|✖|\bERR!|exception", re.I)

BACKEND_CMD = r"""set -e
dn='set -e
dotnet restore backend/FinanceSentry.sln
dotnet build backend/FinanceSentry.sln --configuration Release --no-restore
dotnet build-server shutdown'
if command -v dotnet >/dev/null 2>&1; then
  sh -c "$dn"
else
  img=mcr.microsoft.com/dotnet/sdk:10.0
  docker run --rm -u 0 -v nm-nuget-cache:/nuget "$img" chown "$(id -u):$(id -g)" /nuget
  sh scripts/nm-heavy-slot.sh docker run --rm --memory 2g -u "$(id -u):$(id -g)" \
    -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_NOLOGO=1 -e DOTNET_gcServer=0 \
    -e NUGET_PACKAGES=/nuget -v nm-nuget-cache:/nuget \
    -v "$PWD":/src -w /src "$img" sh -c "$dn"
fi"""

FRONTEND_CMD = "set -e; cd frontend; npm run lint; npm run build; npm run test:ci"


def git(*args):
    return subprocess.run(["git", "-C", ROOT, *args], capture_output=True, text=True)


def changed_files():
    """Changed paths vs the merge-base with origin/main, or None when it cannot be computed."""
    base = git("merge-base", "HEAD", "origin/main")
    if base.returncode != 0:
        return None
    diff = git("diff", "--name-only", base.stdout.strip())
    other = git("ls-files", "--others", "--exclude-standard")
    if diff.returncode != 0 or other.returncode != 0:
        return None
    return sorted(set(diff.stdout.split("\n") + other.stdout.split("\n")) - {""})


def is_docs(path):
    return path.startswith("docs/") or path.endswith(".md")


def halves(files):
    """Which halves to run: a set of 'backend' / 'frontend'."""
    if files is None:
        return {"backend", "frontend"}
    out = set()
    for f in files:
        if is_docs(f):
            continue
        if f.startswith("backend/") or re.fullmatch(r"Directory\.[^/]*\.props", f):
            out.add("backend")
        elif f.startswith("frontend/"):
            out.add("frontend")
        else:
            out.update(("backend", "frontend"))
    return out


def run_half(name):
    override = os.environ.get("FS_STOP_BACKEND_CMD" if name == "backend" else "FS_STOP_FRONTEND_CMD")
    cmd = override or (BACKEND_CMD if name == "backend" else FRONTEND_CMD)
    try:
        proc = subprocess.run(["bash", "-c", cmd], cwd=ROOT, capture_output=True, text=True, timeout=HALF_TIMEOUT)
    except subprocess.TimeoutExpired:
        return 1, f"{name} half timed out after {HALF_TIMEOUT}s"
    except OSError as exc:
        return 1, f"{name} half did not start: {exc}"
    return proc.returncode, proc.stdout + proc.stderr


def summarize(output):
    lines = [l.rstrip() for l in output.splitlines() if l.strip()]
    hits = [l for l in lines if ERROR_RE.search(l)] or lines[-SUMMARY_LINES:]
    return "\n".join(hits[:SUMMARY_LINES])


def main():
    event = hooklib.read_event()
    if event.get("stop_hook_active"):
        hooklib.log("Stop", "-", "skip:stop-hook-active")
        return 0
    start = time.monotonic()
    files = changed_files()
    todo = halves(files)
    if not todo:
        hooklib.log("Stop", "-", "skip:no-code-change", seconds=0)
        return 0
    for name in ("backend", "frontend"):
        if name not in todo:
            continue
        if name == "frontend" and not os.environ.get("FS_STOP_FRONTEND_CMD") \
                and not os.path.isdir(os.path.join(ROOT, "frontend", "node_modules")):
            print("harness warning: frontend/node_modules missing; frontend half skipped (run npm ci)", file=sys.stderr)
            hooklib.log("Stop", name, "warn:no-node-modules")
            continue
        code, output = run_half(name)
        if code != 0:
            hooklib.log("Stop", name, "block:red", seconds=round(time.monotonic() - start, 1))
            print(f"Stop refused: {name} half is red (exit {code}). Fix it before ending the turn:\n"
                  f"{summarize(output)}", file=sys.stderr)
            return 2
    hooklib.log("Stop", "+".join(n for n in ("backend", "frontend") if n in todo), "allow:green",
                seconds=round(time.monotonic() - start, 1))
    return 0


if __name__ == "__main__":
    sys.exit(main())
