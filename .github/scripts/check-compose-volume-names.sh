#!/usr/bin/env bash
# Fails when docker/docker-compose.prod.yml would stop mounting the live data volumes.
#
# The prod stack ran as compose project "docker" (the directory name) before it was pinned to
# `name: finance-sentry`. Compose prefixes each unpinned named volume with the project name, so a
# volume without an explicit `name:` would resolve to an empty finance-sentry_<key> beside the
# docker_<key> that holds the data. This renders the file (read-only, nothing is created or
# started) and asserts the project name and that every declared volume keeps its docker_ name.
#
# Usage: check-compose-volume-names.sh [repo-root]
set -euo pipefail

root="${1:-$(git rev-parse --show-toplevel)}"
file="$root/docker/docker-compose.prod.yml"

# The file requires deploy-time values (${VAR:?...}); any non-empty value renders it.
while read -r var; do
  export "$var=${!var:-placeholder}"
done < <(grep -oE '\$\{[A-Z0-9_]+:\?' "$file" | sed -E 's/^\$\{//; s/:\?$//' | sort -u)

docker compose -f "$file" config --format json | python3 -c '
import json, sys

cfg = json.load(sys.stdin)
errors = []
if cfg["name"] != "finance-sentry":
    errors.append("project name is %r, expected finance-sentry" % cfg["name"])

volumes = cfg.get("volumes") or {}
if not volumes:
    errors.append("no named volumes rendered; postgres_data, api_logs and gateway_keys are expected")
for key, volume in sorted(volumes.items()):
    if volume.get("external"):
        continue
    if volume.get("name") != "docker_" + key:
        errors.append(
            "volume %s resolves to %r, expected docker_%s (pin it with `name:`, or if this volume is "
            "new and deliberately not legacy, say so by editing this check)"
            % (key, volume.get("name"), key)
        )

if errors:
    print("error: docker/docker-compose.prod.yml would not mount the live volumes:", file=sys.stderr)
    for e in errors:
        print("  " + e, file=sys.stderr)
    sys.exit(1)
print("project %s; volumes: %s" % (cfg["name"], ", ".join("%s=%s" % (k, v["name"]) for k, v in sorted(volumes.items()))))
'
