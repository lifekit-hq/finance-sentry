# Security Policy

## Reporting a vulnerability

Use GitHub's private vulnerability reporting: **Security → Report a vulnerability** on
[lifekit-hq/finance-sentry](https://github.com/lifekit-hq/finance-sentry/security/advisories/new).
Please do not open a public issue for a vulnerability, and do not include real
account data in any report.

This is a one-person project: expect an acknowledgement within a few days, not hours.
Only the latest release on `main` is supported.

## Posture (what the code does today)

- **Authentication**: stock ASP.NET Core JwtBearer with a default-deny fallback policy. Every endpoint
  declares `[Authorize]` (optionally with a permission policy) or `[AllowAnonymous]`; the anonymous set is pinned by
  `ApiAuthenticationPipelineTests`. The principal is rebuilt from the local account on every request, so a locked
  or removed account gets 401 at once. Sign-in is OpenID Connect (Logto in the reference deployment); there is no
  self sign-up, accounts are created by invite.
- **Sessions**: the access token lives in memory or in a `__Host-` cookie; the refresh token is an
  httpOnly / Secure / SameSite=Strict cookie.
- **Authorization**: two roles (Owner, Member) that are bundles of `permission` claims; `RoleFeatureMatrixTests` pins the matrix.
- **Tenant isolation**: per-user DbContexts carry an owner query filter, each with a two-user isolation test.
- **Hardening**: per-client rate limits (anonymous and authenticated policies), forwarded headers trusted only from
  configured proxies, a CSP and security headers on the frontend proxy and the gateway, a single-origin CORS policy,
  a state cookie that binds the TrueLayer callback to the initiating browser, and constant-time webhook HMAC checks.
- **Data at rest**: provider credentials (API keys, tokens) are encrypted in the application with AES-256-GCM and
  versioned keys. Transactions, balances and holdings are ordinary PostgreSQL columns: they rely on database access
  control and on disk encryption of the host, which the deployer provides.
- **Backups**: nightly `pg_dump`, encrypted with [age](https://age-encryption.org) and uploaded to S3-compatible
  storage (Cloudflare R2 in the reference deployment); a weekly job restores the newest artifact into a scratch database to verify it
  (see [`docs/DATABASE_BACKUP.md`](../docs/DATABASE_BACKUP.md)).
- **Erasure**: closing an account starts a grace period, after which a purge job runs every module's data eraser.
- **Secrets**: no plaintext secret is committed. The reference deployment keeps its environment in a sops + age
  encrypted file; the CI check `check-sops-recipients.sh` verifies that file's recipients.
- **Telemetry**: the frontend ships no analytics or tracking. The AI agent sends data to the configured model
  provider (Anthropic API or a self-hosted gateway); leave it unconfigured to send nothing.

## Known limits

- No data-export endpoint exists yet (planned; see [`docs/DATA_EXPORT_GUIDE.md`](../docs/DATA_EXPORT_GUIDE.md)).
- Static analysis is CodeQL plus Dependabot; there is no third-party penetration test.
- Self-hosters own their TLS termination, host hardening, key custody and backups.
