#!/usr/bin/env bash
# Fails when the age recipients .sops.yaml names differ from the ones docker/.env.sops is
# actually encrypted to. A recipient added to .sops.yaml only takes effect once someone who
# already holds a key runs `sops updatekeys -y docker/.env.sops`, so the two can drift apart:
# this keeps the config honest about who can decrypt the file.
#
# Reads only the plaintext sops metadata; decrypts nothing and needs no key or sops binary.
#
# Usage: check-sops-recipients.sh [repo-root]
set -euo pipefail

root="${1:-$(git rev-parse --show-toplevel)}"
config="$root/.sops.yaml"
file="$root/docker/.env.sops"

configured=$(grep -v '^[[:space:]]*#' "$config" | grep -oE 'age1[0-9a-z]{58}' | sort -u)
encrypted=$(grep -E '^sops_age__list_[0-9]+__map_recipient=' "$file" | cut -d= -f2 | sort -u)

if [ -z "$configured" ]; then
  echo "error: no age recipient found in .sops.yaml" >&2
  exit 1
fi

if [ "$configured" != "$encrypted" ]; then
  echo "error: docker/.env.sops recipients differ from .sops.yaml" >&2
  echo "  only in .sops.yaml (run sops updatekeys -y docker/.env.sops):" >&2
  comm -23 <(echo "$configured") <(echo "$encrypted") | sed 's/^/    /' >&2
  echo "  only in docker/.env.sops:" >&2
  comm -13 <(echo "$configured") <(echo "$encrypted") | sed 's/^/    /' >&2
  exit 1
fi

echo "docker/.env.sops recipients match .sops.yaml:"
while read -r recipient; do echo "  $recipient"; done <<< "$configured"
