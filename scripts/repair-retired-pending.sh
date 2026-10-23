#!/usr/bin/env bash
#
# One-shot repair for #1229: restore the pending transactions the old pending reconciler retired
# before their settled copy arrived (so they were dropped as duplicates and the payment vanished),
# and settle those the provider now reports as booked.
#
# Run it ONCE, AFTER the date-aware reconciler is deployed - otherwise the next sync retires the
# restored rows again. Idempotent: a restored row is active, so a second run finds nothing and
# changes nothing. Prints before/after counts (eligible, reactivated, settled).
#
# Runs a throwaway instance of the API inside the already-running container (it does NOT start the
# web server), so it reuses the app's credentials, decryption key, and provider clients.
#
# Usage:
#   ./scripts/repair-retired-pending.sh --dry-run   # count what it would restore; reads no provider
#   ./scripts/repair-retired-pending.sh             # do it
#
# Override the container name with API_CONTAINER=... if it differs.
set -euo pipefail

CONTAINER="${API_CONTAINER:-finance-sentry-api}"

exec docker exec "$CONTAINER" dotnet FinanceSentry.API.dll repair-retired-pending "$@"
