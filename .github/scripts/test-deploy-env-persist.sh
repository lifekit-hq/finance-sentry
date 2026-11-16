#!/usr/bin/env bash
# Exercises the docker/.env.deploy persist block of docker/deploy.sh. The required hostname/edge
# values are written all-or-nothing; the optional SEC_EDGAR_CONTACT is written only when non-empty,
# and an unset or empty one never breaks the deploy.
#
# Usage: test-deploy-env-persist.sh [repo-root]
set -euo pipefail

root="${1:-$(git rev-parse --show-toplevel)}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The block under test: from `DEPLOY_ENV_FILE=` to the `fi` that closes its `if` chain.
sed -n '/^DEPLOY_ENV_FILE=/,/^fi$/p' "$root/docker/deploy.sh" > "$work/block.sh"
[ -s "$work/block.sh" ] || { echo "error: persist block not found in docker/deploy.sh" >&2; exit 1; }

fail() { echo "FAIL: $1" >&2; exit 1; }

run() { # <contact value, may be empty> ; writes $work/out.env
  rm -f "$work/out.env"
  ( cd "$work" && env -i PATH="$PATH" JWT_ISSUER=i FRONTEND_BASE_URL=f TRUELAYER_FRONTEND_REDIRECT_BASE=t \
      EDGE_SUBNET=s EDGE_IP_RANGE=r EDGE_BRIDGE_IP=b EDGE_GATEWAY_IP=g ${1+SEC_EDGAR_CONTACT="$1"} \
      bash -euo pipefail -c 'sed "s#^DEPLOY_ENV_FILE=.*#DEPLOY_ENV_FILE=out.env#" block.sh > b.sh; . b.sh' > /dev/null )
}

run "ops@example.test"
grep -qx 'SEC_EDGAR_CONTACT=ops@example.test' "$work/out.env" || fail "set contact: not persisted"
[ "$(stat -c %a "$work/out.env")" = 600 ] || fail "persisted file must be mode 600"
[ "$(grep -c '' "$work/out.env")" = 8 ] || fail "set contact: expected 8 lines"

run ""
grep -q SEC_EDGAR_CONTACT "$work/out.env" && fail "empty contact: must be left out"
[ "$(grep -c '' "$work/out.env")" = 7 ] || fail "empty contact: expected 7 lines"

run
grep -q SEC_EDGAR_CONTACT "$work/out.env" && fail "unset contact: must be left out"

echo "deploy.sh deploy env persistence: ok"
