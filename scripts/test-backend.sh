#!/bin/sh
# Full backend build + test in a 2 GB SDK container (docker/docker-compose.test.yml).
# Without Docker: cd backend && dotnet test FinanceSentry.sln -c Release (same limits via Directory.Build.rsp).
set -e
cd "$(dirname "$0")/../docker"
docker run --rm -u 0 -v fs-backend-tests-nuget:/nuget mcr.microsoft.com/dotnet/sdk:10.0 chown "$(id -u):$(id -g)" /nuget >/dev/null 2>&1 || true
HOST_UID=$(id -u) HOST_GID=$(id -g) exec docker compose -f docker-compose.test.yml run --rm backend-tests
