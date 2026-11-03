#!/usr/bin/env bash
#
# One-shot repair of the Owner's July-August 2026 net-worth history (numbers report R1): removes the
# Monobank credit limit (90,000 UAH) the banking sleeve counted as own funds before #531, and re-derives
# the brokerage sleeve on the days it was $0 or a frozen quote (07-02, 07-03, 07-11, 07-14..07-20).
#
# Default is a DRY RUN: prints every planned day, old against new, and writes nothing (no backup table
# either). --apply copies the originals to net_worth_snapshots_backup_july_2026 (and the per-source rows to
# net_worth_snapshot_sources_backup_july_2026) first, then rewrites the rows in one transaction. Idempotent:
# a second --apply finds every row already repaired and changes nothing. Refuses to apply (changing nothing)
# if any planned day is missing or holds a total the repair was not written for.
#
# Runs a throwaway instance of the API inside the already-running container (it does NOT start the web server).
#
# Usage:
#   ./scripts/repair-net-worth-history.sh            # dry run
#   ./scripts/repair-net-worth-history.sh --apply    # do it
#   ./scripts/repair-net-worth-history.sh --user <guid> [--apply]   # only when more than one Owner exists
#
# Override the container name with API_CONTAINER=... if it differs.
set -euo pipefail

CONTAINER="${API_CONTAINER:-finance-sentry-api}"

exec docker exec "$CONTAINER" dotnet FinanceSentry.API.dll repair-net-worth-history "$@"
