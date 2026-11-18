#!/usr/bin/env python3
"""PreToolUse: block Edit/Write on protected paths (exit 2, one-line reason on stderr)."""
import fnmatch
import sys

import hooklib

RULES = [
    ("migrations", lambda p: "Migrations" in p.split("/")[:-1],
     "applied migrations are immutable; add a new migration instead"),
    ("eslint-config", lambda p: p == "frontend/eslint.config.mjs", "lint config is not ours to loosen"),
    ("prettier-config", lambda p: fnmatch.fnmatchcase(p, "frontend/.prettierrc*"), "format config is not ours to change"),
    ("editorconfig", lambda p: p == "backend/.editorconfig", "code-style config is not ours to change"),
    ("directory-build-props", lambda p: p == "backend/Directory.Build.props", "build gates are not ours to change"),
    ("screen-baselines", lambda p: fnmatch.fnmatchcase(p, "frontend/e2e/screens/*.png"),
     "screen baselines come from the update-screen-baselines workflow"),
]


def main():
    event = hooklib.read_event()
    if event.get("tool_name") not in ("Edit", "Write"):
        return 0
    path = hooklib.target_path(event)
    if path is None:
        return 0
    for name, matches, why in RULES:
        if matches(path):
            hooklib.log("PreToolUse", path, "block:" + name)
            print(f"BLOCKED by rule '{name}' ({path}): {why}. Open an issue, or state the reason in the plan.",
                  file=sys.stderr)
            return 2
    hooklib.log("PreToolUse", path, "allow")
    return 0


if __name__ == "__main__":
    sys.exit(main())
