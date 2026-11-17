---
paths:
  - "backend/src/**/Infrastructure/**/*DbContext.cs"
  - "backend/src/**/Infrastructure/Persistence/**/*.cs"
  - "backend/src/**/Infrastructure/Jobs/**/*.cs"
  - "backend/src/**/Domain/Repositories/**/*.cs"
  - "backend/src/**/Application/Jobs/**/*.cs"
---

# Owner Query Filter - per-user DbContexts

**Why it exists:** per-user data is isolated by a named EF query filter, not by every query remembering a `UserId` predicate. Rollout history: #947, #1045, #1047 (shared `OwnerScopedDbContext`, `AuthDbContext` conventions); #1086 (account closure reads across people). A sync upsert or dedup read without the opt-out finds nothing under no principal and re-inserts duplicates.
**Retire when:** an analyzer or architecture test fails the build on an unfiltered per-user entity, or a cross-user read without `IgnoreQueryFilters([OwnerQueryFilter.Name])`; then this file shrinks to a pointer to that check.

A DbContext converted to owner scoping takes `ICurrentUser` (`FinanceSentry.Core.Auth`) and carries the named filter on every per-user entity: declared by hand as `HasQueryFilter(OwnerQueryFilter.Name, e => e.UserId == CurrentUserId)`, or - in a context derived from `OwnerScopedDbContext` (`WealthDbContext`, `BudgetsDbContext`, `EventsDbContext`, `RiskDbContext`, `AgentDbContext`, `AlertsDbContext`, `AnalyticsDbContext`, `BankSyncDbContext`, `BrokerageSyncDbContext`, `CryptoSyncDbContext`, `CompanionDbContext`, `ResearchDbContext` so far) - applied by the base to every entity that implements `IUserOwned`. A request sees only its principal's rows; a background job has no principal, so the filter matches nothing.

- Every job or sweep that legitimately reads across users opts out explicitly with `IgnoreQueryFilters([OwnerQueryFilter.Name])` and keeps its own `UserId` predicate. Inserts and `SaveChanges` are not filtered; `ExecuteUpdate`/`ExecuteDelete` are.
- Each converted context ships a two-user isolation test and a no-principal test per cross-user job (see `AlertsOwnerQueryFilterTests`).
- Hosts register `ICurrentUser` explicitly (API: `HttpContextCurrentUser`; MCP: `IdentityResolverCurrentUser`); design-time factories pass `NoCurrentUser.Instance`.
- Repository reads that jobs, anonymous callbacks or cross-module readers need are separate `…Unscoped…` methods that opt out with `IgnoreQueryFilters([OwnerQueryFilter.Name])` and keep their own `UserId` predicate; the unsuffixed methods stay filtered for the request path. A no-principal caller must never use an unsuffixed read.
- An entity with a shared corpus (`ResearchDocument`, `RadarSignal`: `UserId == null` rows are visible to everyone) has a nullable `UserId`, so it cannot implement `IUserOwned`: it declares `e.UserId == null || e.UserId == CurrentUserId` by hand, reading the context's `CurrentUserId`; owned rows stay owner-only. `RadarDbContext` holds no `IUserOwned` entity, so it stays a plain `DbContext` that applies `PersistenceConventions` itself.
- A string `UserId` column (`DetectedSubscription`, `RefreshToken`, `McpAuthorizationCode`) holds `Guid.ToString()`; its context compares against `currentUser.UserId?.ToString()` in that same form.
- Converted so far: `AgentDbContext`, `AlertsDbContext`, `AnalyticsDbContext`, `AuthDbContext`, `BankSyncDbContext`, `BrokerageSyncDbContext`, `BudgetsDbContext`, `CompanionDbContext`, `CryptoSyncDbContext`, `EventsDbContext`, `RadarDbContext`, `ResearchDbContext`, `RiskDbContext`, `SubscriptionsDbContext`, `WealthDbContext`. A child entity with no `UserId` of its own (an Agent `Message`) declares the filter through its parent's owner (a subquery on the parent set), so the per-user test asserts every entity type, not only those with a `UserId`. Sync upserts and dedup reads must opt out too: with no principal a filtered existence check finds nothing and re-inserts duplicates.
- `AuthDbContext` filters only `RefreshToken` and `McpAuthorizationCode`; Identity's own tables, `McpServiceToken` and `AccountClosure` stay unfiltered (read before a person is in scope or across people: an Owner lists and reopens others' closures, the purge job runs with no principal). Every read of a filtered row runs on an anonymous endpoint, so those reads are `…Unscoped…` and match on the token/code hash; `RevokeAsync(userId)` keeps its name, opts out and scopes by the named user. It is a plain-`DbContext` exception like `SubscriptionsDbContext`: it derives from `IdentityDbContext<ApplicationUser>` (string Identity ids, not the `Guid` `IUserOwned` key `OwnerScopedDbContext` scopes), so it declares those two filters by hand and applies `PersistenceConventions` itself in `ConfigureConventions`, without the Guid-key default or decimal precision (its schema has neither); its design-time factory is a `DesignTimeDbContextFactoryBase`.
