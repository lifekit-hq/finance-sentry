---
paths:
  - "frontend/src/app/**/*.component.ts"
  - "frontend/src/app/**/*.html"
  - "frontend/src/app/shared/pipes/**/*.ts"
---

# UI library first

**Why it exists:** components built in the host app drift from the design system and fork behaviour the library already has (raw `<input>`, hand-rolled Retry buttons, per-module money pipes, `if/else` error ladders). `lifekit/no-raw-controls` only warns and cannot tell a missing component from a justified one.
**Retire when:** the library lint rules (`lifekit/no-raw-controls`, `lifekit/no-local-currency-format`) are errors and cover async-state and error-message use, leaving only the "new components go to lifekit-common" rule as prose.

**Any new UI component MUST be created in [`lifekit-hq/lifekit-common`](https://github.com/lifekit-hq/lifekit-common) first** (`@lifekit-hq/ui`, Storybook-first - see that repo's CLAUDE.md), then consumed here as a published package. Components are never built directly in the host Angular app (`frontend/`). The `cmn-` selector prefix is reserved for library components.

**Before writing any Angular template or UI element**, check the catalog - the hosted Storybook at https://lifekit-hq.github.io/lifekit-common/ or `node_modules/@lifekit-hq/ui` (after `npm ci` in `frontend/` - a reused worktree's tree can lag `package.json`). Use `cmn-button`, `cmn-input`, `cmn-form-field`, `cmn-alert`, `cmn-card`, etc.

**Registry auth**: `@lifekit-hq/*` installs from GitHub Packages - `frontend/.npmrc` expects `NODE_AUTH_TOKEN` (`export NODE_AUTH_TOKEN=$(gh auth token)`, token needs `read:packages`). CI passes `secrets.GITHUB_TOKEN`; Docker builds take it as the `npm_token` BuildKit secret.

**Local iteration against unpublished library changes**: in `lifekit-common` run `npm run build && npm pack ./dist/lifekit-hq/ui`, then here `npm install <tarball>` (never commit a tarball reference). Day-to-day component development happens in lifekit-common's Storybook.

## Money display

Amounts are symbol-first (`$18,981.48`), with an ISO code prefix only where the symbol is ambiguous, `—` for null/NaN, and a base-currency equivalent muted on its own second line; conversion and aggregation stay in [`docs/money-semantics.md`](../../docs/money-semantics.md) (lint enforces the single `money` pipe).

## Error message resolution

Error-code → user-message mapping is centralized; **do not** add an `if/else` ladder in a component or store.

- Mechanism lives in `@lifekit-hq/ui`: `ERROR_MESSAGES` token + `ErrorMessageService.resolve(code)` → `string | null`.
- The registry is `src/app/core/errors/error-messages.registry.ts` (flat `Record<string, string>` of all backend `errorCode` values), wired via `provideErrorMessages()`.
- Stores consume via `inject(ErrorMessageService)` inside `*.computed.ts`, falling back to a feature-specific default when `resolve()` returns `null`.
- **A new backend error code appends its message to the registry in the same PR.** Stores read the code with `extractErrorCode(err)` from `@lifekit-hq/core` (only recognizes a real `HttpErrorResponse`, so specs throw `new HttpErrorResponse({status, error: {errorCode}})`); do not write a local extractor.
- **Exception - server-composed messages:** `ACCOUNT_LOCKED` (429 + `Retry-After`) is deliberately not in the registry: its text carries the remaining wait, so `auth.computed.ts` shows the server's `error` text (`ErrorUtils.extractMessage`, kept in `AuthStore.errorDetail`) for that code only.

## Async state - `cmn-async-state`

A region that swaps between loading, error, empty and content uses `cmn-async-state` from `@lifekit-hq/ui` (`[status]`, `[errorMessage]`, `[errorPlacement]`, `[isEmpty]`, `[emptyMessage]` / `[emptySubMessage]` / `[emptyIcon]`, `retryable` + `(retry)`, `[skeletonRows]`, `[skeletonHeight]`) instead of local `@if (...Loading())` / error branching. Each page derives an `AsyncStateStatus` computed (`loading` > `error` > `success`) and projects into the slots `[skeleton]`, `[empty]` (rich empty state) and `[error-action]`. `errorPlacement="above"` keeps content under a persistent banner; the default replaces it. A plain "try again" is the primitive's `retryable` button wired to `(retry)="store.load()"` - never a hand-rolled Retry button; a plain empty message takes `emptyMessage` / `emptySubMessage` / `emptyIcon`, and only an empty state with a CTA or its own layout goes in `[empty]`.

Dashboard, accounts-list and transaction-ledger derive that status through `AsyncViewUtils.resolve` (`shared/utils/async-view.utils.ts`) instead of a local computed: a retry keeps the stale data instead of a skeleton, an error never reads as empty, and while offline (`ConnectivityService`) data on screen stays with a "last synced" notice above it. Their stores record `lastSyncedAt` on each successful load, and the Retry in `[error-action]` repeats the failed request.

A list bound to a query (the budgets month) passes `hasData: false` to `AsyncViewUtils.resolve` and keeps the default placement, so a reload shows the skeleton and a failure replaces the previous query's rows with the error; transactions and alerts instead keep the previous query's rows on screen, dimmed, until the new first page lands (`keepContent` on `cmn-async-state`, `keepContent: true` to `resolve`), and drop them when that load fails for a different query - the transactions ledger records the filter its rows were loaded for (`rowsFilter`), so a failed revalidation of rows that still match keeps them under the error banner or the offline "last synced" notice, and a next page is asked only while the filter still equals `rowsFilter`; a list that reloads in place (people, subscriptions, the fired feed, the commitment picker) passes its real `hasData`, renders `errorPlacement="above"` and keeps its rows under the banner. People, subscriptions and the commitment picker store no `lastSyncedAt` and render no offline notice: they hand `resolve` `offline` only while nothing is on screen, so a failed reload with rows still shows its error banner instead of reading as a successful offline load. Budgets keeps its list under the banner only when the refresh after an edit fails (`refreshFailed`, `errorPlacement="above"`); a failed month load replaces it.

Adopters: `events` (calendar and fired), `truelayer-picker`, `inzhur-form`, `holdings`, `asset-dossier`, `dashboard`, `flow-breakdown`, `accounts-list`, `transaction-ledger`, `people`, `budgets`, `subscriptions`, `alerts`, `add-commitment-dialog`.

When the primitive wraps several children, they are no longer direct children of the old parent, so the parent's spacing (`space-y-cmn-5` of the old page container, or the page's own `space-y-*`) goes on the `cmn-async-state` host. Since `@lifekit-hq/ui` 4.0 turns `keepContent` on by default, which puts the content in one wrapper `div`, a host whose own `space-y-*`, `gap-*` or flex column lays out those children (dashboard, subscriptions, asset dossier, events, flow breakdown, budgets) passes `[keepContent]="false"` and `[crossfade]="false"` (`crossfade` defaults to on and wraps the content too); transactions and alerts keep content and lay out inside the wrapper.

Remaining local markup (a gap in the primitive, not a surface left unconverted): `dashboard` per-widget inline skeletons inside live cards (net worth, window tiles) - there is no region swap, so the primitive only carries the persistent error banner and the connect-account empty state. Drop this when the primitive can express per-widget loading.

A list longer than one page (transactions, alerts, the fired feed) ends in `lk-infinite-list`, never a Load More button or a pager: the store keeps the next page's load under its own named async status (`withNamedAsyncStatus('append')`) and binds `hasMore` / `loading` / `failed`, and both `lk-infinite-list-load-more` and `lk-infinite-list-retry` call the store's next-page method. A revisit repaints the last data at once and revalidates it through `withSessionCache` (`shared/store/session-cache/`, memory only, cleared on sign-out); the transactions ledger and the dashboard use it. Alerts needs no cache (its store is app-wide and loads once). The content fades in (`crossfade`) only where it replaces that visit's skeleton - the ledger and alerts use a plain `crossfade`, and `cmn-async-state` itself skips the fade on a revisit that already has data.
