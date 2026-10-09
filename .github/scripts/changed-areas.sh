#!/usr/bin/env bash
# Classifies the files a pull request changes into CI areas and writes
# backend=/frontend=/docker=/docker_frontend= (true|false) lines to $GITHUB_OUTPUT.
# `docker` covers the .NET images (api, mcp, gateway); `docker_frontend` the frontend image.
#
# Usage: changed-areas.sh <own-workflow-file>
#
# Fail-safe by design: anything outside the known areas, a non-PR event, or a
# failed diff turns every area on, so a skip only happens for files that are
# positively known not to affect that area.
set -uo pipefail

own_workflow="${1:?usage: changed-areas.sh <own-workflow-file>}"
out="${GITHUB_OUTPUT:-/dev/stdout}"

emit() {
  { echo "backend=$1"; echo "frontend=$2"; echo "docker=$3"; echo "docker_frontend=$4"; } >> "$out"
  echo "areas: backend=$1 frontend=$2 docker=$3 docker_frontend=$4"
}

if [ "${GITHUB_EVENT_NAME:-}" != "pull_request" ]; then
  echo "non-PR event (${GITHUB_EVENT_NAME:-unset}): running everything"
  emit true true true true
  exit 0
fi

# The PR checkout is the merge commit; its first parent is the current base tip.
if ! files=$(git diff --name-only HEAD^1 HEAD) || [ -z "$files" ]; then
  echo "could not compute the changed-file list: running everything"
  emit true true true true
  exit 0
fi

backend=false frontend=false docker=false docker_frontend=false
while IFS= read -r f; do
  case "$f" in
    # Area inputs first: markdown under them (e.g. agent/ledger/*.md) is still an input.
    frontend/*) frontend=true docker_frontend=true ;;
    backend/*|agent/*|global.json|.config/*) backend=true docker=true ;;
    docker/Dockerfile|docker/Dockerfile.mcp|docker/Dockerfile.gateway) docker=true ;;
    # Shared by the frontend image and the e2e server (frontend/e2e/serve.mjs); the nginx config and
    # image recipe also decide what the cold-load LCP check measures.
    docker/nginx.security-headers.conf|docker/nginx.frontend.conf|docker/Dockerfile.frontend.prod) frontend=true docker_frontend=true ;;
    docker/nginx.*) docker_frontend=true ;;
    # The frontend image bundles it into the What's new panel (frontend/scripts/build-whats-new.mjs).
    CHANGELOG.md) docker_frontend=true ;;
    .dockerignore) docker=true docker_frontend=true ;;
    .github/workflows/"$own_workflow") backend=true frontend=true docker=true docker_frontend=true ;;
    .github/scripts/*) backend=true frontend=true docker=true docker_frontend=true ;;
    .github/actions/build-image/*) docker=true docker_frontend=true ;;
    # Files that no CI area builds or tests.
    docs/*|specs/*|.specify/*|.claude/*|.qwen/*|.devclaw/*|.vscode/*|.husky/*|.github/*|*.md|LICENSE) ;;
    *) echo "unclassified path '$f': running everything"; backend=true frontend=true docker=true docker_frontend=true ;;
  esac
done <<< "$files"

emit "$backend" "$frontend" "$docker" "$docker_frontend"
