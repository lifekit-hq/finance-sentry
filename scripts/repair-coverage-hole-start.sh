#!/usr/bin/env bash
#
# One-shot repair of the open coverage holes (follow-up to #1238): moves each open hole's start back to the first quiet
# week of its gap. The sentinel used to open a hole only as far back as an unbroken run of quiet weeks, so one stray
# purchase week inside a long gap left the weeks before it outside the hole and budgets still trained on them.
#
# Default is a DRY RUN: prints every open hole, old start against new, and writes nothing (no backup table either).
# --apply copies the original rows to bank_sync.coverage_holes_backup_start first, then moves the start in one
# transaction. Idempotent: a second --apply finds every hole at its first quiet week and changes nothing. Only an open
# hole's start is touched, and only earlier.
#
# Runs a throwaway instance of the API inside the already-running container (it does NOT start the web server).
#
# Usage:
#   ./scripts/repair-coverage-hole-start.sh                        # dry run
#   ./scripts/repair-coverage-hole-start.sh --apply                # do it
#   ./scripts/repair-coverage-hole-start.sh --account <guid> [--apply]   # only that account's open hole
#
# Override the container name with API_CONTAINER=... if it differs.
set -euo pipefail

CONTAINER="${API_CONTAINER:-finance-sentry-api}"

exec docker exec "$CONTAINER" dotnet FinanceSentry.API.dll repair-coverage-hole-start "$@"
