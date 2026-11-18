#!/bin/sh
# Backend restore, build and every non-integration test project: the backend half of
# `.no-mistakes.yaml` commands.test. Run by .husky/pre-push (the Stop hook only builds).
# dotnet on PATH, else the 2 GB SDK container behind the 2-slot nm-heavy-slot flock.
set -e
cd "$(dirname "$0")/.."
dn='set -e
dotnet restore backend/FinanceSentry.sln
dotnet build backend/FinanceSentry.sln --configuration Release --no-restore
dotnet build-server shutdown
for p in backend/tests/*/*.csproj; do
  case "$p" in *Tests.Integration*) continue ;; esac
  dotnet test "$p" --configuration Release --no-build --verbosity normal
done'
if command -v dotnet >/dev/null 2>&1; then
  exec sh -c "$dn"
fi
img=mcr.microsoft.com/dotnet/sdk:10.0
docker run --rm -u 0 -v nm-nuget-cache:/nuget "$img" chown "$(id -u):$(id -g)" /nuget
exec sh scripts/nm-heavy-slot.sh docker run --rm --memory 2g -u "$(id -u):$(id -g)" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e DOTNET_NOLOGO=1 -e DOTNET_gcServer=0 \
  -e NUGET_PACKAGES=/nuget -v nm-nuget-cache:/nuget \
  -v "$PWD":/src -w /src "$img" sh -c "$dn"
