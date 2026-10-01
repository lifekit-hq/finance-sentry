#!/usr/bin/env bash
# Prune old finance-sentry images from the deploy host.
#
# Deploy-by-SHA pulls a fresh image per service per commit and nothing else removes them
# (deploy.sh only drops dangling layers). For each published service this keeps the newest
# KEEP_NEWEST commit-SHA-tagged images and removes the rest.
#
# Safety:
# - Only ghcr.io/lifekit-hq/finance-sentry-<service> repositories, only 40-hex SHA tags
#   (:local, :main and every other repository are never touched).
# - An image used by any container, running or stopped, is never removed.
# - Plain `docker image rm` (no -f): Docker itself refuses images still in use.
# - Never stops or restarts anything; no `image prune -a` / `system prune`; volumes untouched.
#
# Usage: docker/prune-host-images.sh [--dry-run]     (retention is the KEEP_NEWEST constant below)
# Newest = most recently built (image creation time), which tracks commit order.

set -euo pipefail

REGISTRY="ghcr.io/lifekit-hq"
SERVICES=(api mcp gateway frontend)
KEEP_NEWEST=5

dry_run=false
case "${1:-}" in
  "") ;;
  --dry-run) dry_run=true ;;
  *)
    echo "usage: $0 [--dry-run]" >&2
    exit 2
    ;;
esac

# Image IDs behind every container (running or stopped).
in_use="$(docker ps -aq | xargs -r docker inspect --format '{{.Image}}' | sort -u)"

mode="removing"
$dry_run && mode="dry run, would remove"
echo "[prune] keep newest $KEEP_NEWEST SHA-tagged images per service ($mode the rest)"

removed=0
for service in "${SERVICES[@]}"; do
  repo="$REGISTRY/finance-sentry-$service"
  # "<created> <full image id> <tag>", newest first.
  rows="$(docker image ls "$repo" --no-trunc --format '{{.Tag}} {{.ID}}' \
    | awk '$1 ~ /^[0-9a-f]{40}$/' \
    | while read -r tag id; do
        echo "$(docker image inspect --format '{{.Created}}' "$id") $id $tag"
      done | sort -r)"
  index=0
  while read -r _ id tag; do
    [[ -n ${tag:-} ]] || continue
    index=$((index + 1))
    if [[ $index -le $KEEP_NEWEST ]]; then
      continue
    fi
    if grep -qxF "$id" <<<"$in_use"; then
      echo "[prune] keep   $repo:$tag (in use by a container)"
      continue
    fi
    if $dry_run; then
      echo "[prune] would remove $repo:$tag"
    elif docker image rm "$repo:$tag" >/dev/null; then
      echo "[prune] removed $repo:$tag"
    else
      echo "[prune] warn: could not remove $repo:$tag" >&2
      continue
    fi
    removed=$((removed + 1))
  done <<<"$rows"
done

echo "[prune] done — $removed image(s) $($dry_run && echo 'would be ')removed"
