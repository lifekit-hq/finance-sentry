"""Shared helpers for the Claude Code hooks. Plain Python, no model call."""
import datetime
import json
import os
import sys

ROOT = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def read_event():
    try:
        return json.load(sys.stdin)
    except (ValueError, OSError):
        return {}


def target_path(event):
    """Repo-relative POSIX path of the file the tool call touched, or None."""
    path = (event.get("tool_input") or {}).get("file_path")
    if not isinstance(path, str) or not path:
        return None
    root = os.path.realpath(event.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR") or ROOT)
    full = os.path.realpath(path if os.path.isabs(path) else os.path.join(root, path))
    try:
        rel = os.path.relpath(full, ROOT)
    except ValueError:
        return None
    return rel.replace(os.sep, "/")


def log(hook, file, decision, **extra):
    """Append one JSON line {ts, hook, file, decision, ...extra}; never raises."""
    path = os.environ.get("FS_HOOK_LOG") or os.path.join(ROOT, ".claude", "hooks.log.jsonl")
    line = {
        "ts": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "hook": hook,
        "file": file,
        "decision": decision,
    }
    line.update(extra)
    try:
        with open(path, "a", encoding="utf-8") as fh:
            fh.write(json.dumps(line) + "\n")
    except OSError:
        pass
