#!/usr/bin/env bash
# Runs the Claude Code hook tests (blocked + allowed case per hook). No model call, no network.
set -euo pipefail
exec python3 -m unittest discover -v -s "$(dirname "${BASH_SOURCE[0]}")" -p 'test_*.py'
