#!/usr/bin/env python3
"""PostToolUse: after an Edit/Write, auto-fix the touched file and report what is left.

.ts under frontend/ -> `eslint --fix <file>`; .cs -> `dotnet format <nearest csproj> --include <file>`.
Only the touched file is passed to the fixer. Remaining violations go back to the model as
additionalContext. A missing tool fails open with a logged warning.
"""
import json
import os
import shlex
import shutil
import subprocess
import sys

import hooklib


def nearest_project(path):
    d = os.path.dirname(os.path.join(hooklib.ROOT, path))
    while d.startswith(hooklib.ROOT) and d != hooklib.ROOT:
        for name in sorted(os.listdir(d)):
            if name.endswith(".csproj"):
                return os.path.join(d, name)
        d = os.path.dirname(d)
    return None


def build_command(path):
    full = os.path.join(hooklib.ROOT, path)
    if path.startswith("frontend/") and path.endswith(".ts"):
        override = os.environ.get("FS_HOOK_ESLINT")
        argv = shlex.split(override) if override else ["npx", "--no-install", "eslint"]
        return argv + ["--fix", os.path.relpath(full, os.path.join(hooklib.ROOT, "frontend"))], \
            os.path.join(hooklib.ROOT, "frontend")
    if path.endswith(".cs"):
        override = os.environ.get("FS_HOOK_DOTNET")
        argv = shlex.split(override) if override else ["dotnet"]
        proj = nearest_project(path)
        if proj is None:
            return None, None
        return argv + ["format", proj, "--include", full], hooklib.ROOT
    return None, None


def main():
    event = hooklib.read_event()
    if event.get("tool_name") not in ("Edit", "Write"):
        return 0
    path = hooklib.target_path(event)
    if path is None:
        return 0
    argv, cwd = build_command(path)
    if argv is None:
        hooklib.log("PostToolUse", path, "skip")
        return 0
    if shutil.which(argv[0]) is None:
        hooklib.log("PostToolUse", path, "warn:tool-missing:" + argv[0])
        print(f"harness warning: {argv[0]} not found; skipped auto-fix of {path}", file=sys.stderr)
        return 0
    try:
        proc = subprocess.run(argv, cwd=cwd, capture_output=True, text=True, timeout=240)
    except (OSError, subprocess.TimeoutExpired) as exc:
        hooklib.log("PostToolUse", path, "warn:tool-failed")
        print(f"harness warning: auto-fix of {path} did not run: {exc}", file=sys.stderr)
        return 0
    output = (proc.stdout + proc.stderr).strip()
    if proc.returncode == 0:
        hooklib.log("PostToolUse", path, "fixed-clean")
        return 0
    hooklib.log("PostToolUse", path, "violations")
    context = f"Auto-fix ran on {path} but violations remain; fix them:\n{output[-4000:]}"
    json.dump({"hookSpecificOutput": {"hookEventName": "PostToolUse", "additionalContext": context}}, sys.stdout)
    return 0


if __name__ == "__main__":
    sys.exit(main())
