#!/usr/bin/env bash
# Exercises the legacy-project retirement block of docker/deploy.sh against a stub `docker`, so no
# daemon, volume or container is touched. The block must (1) bring down compose project "docker"
# without -v when its containers came from the prod compose file, (2) do nothing when there is no
# such project, (3) do nothing for a project "docker" from some other compose file.
#
# Usage: test-deploy-legacy-project.sh [repo-root]
set -euo pipefail

root="${1:-$(git rev-parse --show-toplevel)}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# The block under test: from `legacy_files=` to the `fi` that closes its `if`.
sed -n '/^legacy_files=/,/^fi$/p' "$root/docker/deploy.sh" > "$work/block.sh"
[ -s "$work/block.sh" ] || { echo "error: legacy-project block not found in docker/deploy.sh" >&2; exit 1; }

mkdir "$work/bin"
cat > "$work/bin/docker" <<'STUB'
#!/usr/bin/env bash
if [ "$1" = ps ]; then printf '%s' "$STUB_PS_OUTPUT"; exit 0; fi
echo "$*" >> "$STUB_LOG"
STUB
chmod +x "$work/bin/docker"

run() { # <ps output>
  : > "$work/log"
  STUB_LOG="$work/log" STUB_PS_OUTPUT="$1" PATH="$work/bin:$PATH" DEPLOY_ENV_FILE=docker/.env.deploy \
    bash -euo pipefail "$work/block.sh" > /dev/null
}

fail() { echo "FAIL: $1" >&2; exit 1; }

run "/srv/finance-sentry/docker/docker-compose.prod.yml"
expected="compose -p docker -f docker/docker-compose.prod.yml --env-file docker/.env --env-file docker/.env.deploy down"
[ "$(cat "$work/log")" = "$expected" ] || fail "legacy project: expected '$expected', got '$(cat "$work/log")'"
grep -qE '(^| )(-v|--volumes|volume|network|prune)( |$)' "$work/log" && fail "legacy teardown must not remove volumes or networks itself"

run ""
[ ! -s "$work/log" ] || fail "no legacy project: expected no docker calls, got '$(cat "$work/log")'"

run "/srv/other-stack/docker/docker-compose.yml"
[ ! -s "$work/log" ] || fail "foreign project 'docker': expected no docker calls, got '$(cat "$work/log")'"

echo "deploy.sh legacy-project retirement: ok"
