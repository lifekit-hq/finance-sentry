#!/usr/bin/env bash
# Classifies the files a pull request changes into CI areas and writes
# backend=/frontend=/docker= (true|false) lines to $GITHUB_OUTPUT.
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
  { echo "backend=$1"; echo "frontend=$2"; echo "docker=$3"; } >> "$out"
  echo "areas: backend=$1 frontend=$2 docker=$3"
}

if [ "${GITHUB_EVENT_NAME:-}" != "pull_request" ]; then
  echo "non-PR event (${GITHUB_EVENT_NAME:-unset}): running everything"
  emit true true true
  exit 0
fi

# The PR checkout is the merge commit; its first parent is the current base tip.
if ! files=$(git diff --name-only HEAD^1 HEAD) || [ -z "$files" ]; then
  echo "could not compute the changed-file list: running everything"
  emit true true true
  exit 0
fi

backend=false frontend=false docker=false
while IFS= read -r f; do
  case "$f" in
    # Files that no CI area builds or tests.
    docs/*|specs/*|.specify/*|.claude/*|.qwen/*|.devclaw/*|.vscode/*|.husky/*|*.md|LICENSE) ;;
    .github/workflows/"$own_workflow") backend=true frontend=true docker=true ;;
    .github/scripts/*) backend=true frontend=true docker=true ;;
    .github/*) ;;
    frontend/*) frontend=true ;;
    backend/*|agent/*|global.json|.config/*) backend=true docker=true ;;
    docker/Dockerfile|.dockerignore) docker=true ;;
    *) echo "unclassified path '$f': running everything"; backend=true frontend=true docker=true ;;
  esac
done <<< "$files"

emit "$backend" "$frontend" "$docker"
