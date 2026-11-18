#!/usr/bin/env bash
# Runs the harness tests: Claude Code hooks (blocked + allowed case per hook) the outer-loop attribution script and the gate-contract document check. No model call, no network.
set -euo pipefail
exec python3 -m unittest discover -v -s "$(dirname "${BASH_SOURCE[0]}")" -p 'test_*.py'
