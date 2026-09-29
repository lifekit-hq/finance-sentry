# syntax=docker/dockerfile:1
# Multi-stage build for FinanceSentry API
# Build context: repository root

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Restore into the layer filesystem (not a cache mount): a cache mount's contents are
# non-persistent under BuildKit GC/concurrent-build pressure, so a later RUN (publish)
# reading packages restored by an earlier RUN can find them missing (MSB3030/NETSDK1152).
ENV NUGET_PACKAGES=/src/.nuget/packages

COPY backend/*.sln backend/Directory.*.props backend/
COPY --parents backend/**/*.csproj ./

WORKDIR /src/backend
RUN dotnet restore FinanceSentry.sln

WORKDIR /src
COPY backend/ backend/
# 040: the versioned Ledger persona lives at the repo root (outside backend/); the API project links
# these files into its publish output so PersonaComposer can compose the system prompt at runtime.
COPY agent/ agent/

WORKDIR /src/backend
RUN dotnet build FinanceSentry.sln -c Release --no-restore

# Backend CI already runs the unit tests with coverage; running them again here just
# duplicates the run and lets test flakiness fail the image build too.
RUN dotnet publish src/FinanceSentry.API/FinanceSentry.API.csproj -c Release -o /app/publish --no-build

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# curl/tzdata for health + timezones; postgresql16-client (pg_dump/pg_restore/createdb/dropdb) + age
# power the feature-024 off-host backup + restore-drill jobs. A v16 client dumps/restores the v14
# server (supported newer-client→older-server direction); backups no-op when BACKUP_* is unset.
# apk upgrade first: the aspnet base image lags Alpine security releases (openssl/libpq CVEs)
# and the Trivy HIGH/CRITICAL gate in docker-build.yml fails the build until they're patched.
RUN apk upgrade --no-cache && \
    apk add --no-cache curl tzdata postgresql16-client age krb5-libs

COPY --from=build /app/publish .

RUN mkdir -p /app/logs && chown -R nobody:nobody /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 5000

HEALTHCHECK --interval=10s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:5000/api/v1/health || exit 1

USER nobody

ENTRYPOINT ["dotnet", "FinanceSentry.API.dll"]
