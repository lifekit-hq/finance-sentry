# Frontend Rules (mandatory gates)

## Frontend ESLint — mandatory gate

After writing or modifying **any** Angular `.ts` file, run `npx eslint <file>` from `frontend/` and fix all errors before moving on. Non-negotiable rules (see constitution § II for the full list):
- `inject()` only — no constructor parameter injection
- `ChangeDetectionStrategy.OnPush` on every component
- Do **not** add `standalone: true` to `@Component` / `@Pipe` / `@Directive` — it is the default in Angular 19+ and is dead boilerplate
- Selector prefix: `fns-` (e.g. `fns-login`, `fns-dashboard`)
- Explicit access modifiers on all class members (`public`/`private`)
- No magic numbers — extract to named constants
- camelCase class properties, no underscore prefix
- Run `eslint --fix` after writing imports (auto-sorts + auto-formats)

---

## UI Component Library Rule

**Any new UI component MUST be created in [`lifekit-hq/lifekit-common`](https://github.com/lifekit-hq/lifekit-common) first** (`@lifekit-hq/ui`, Storybook-first — see that repo's CLAUDE.md), then consumed here as a published package. Components are never built directly in the host Angular app (`frontend/`). The `cmn-` selector prefix is reserved for library components.

**Before writing any Angular template or UI element**, check the component catalog first — the hosted Storybook at https://lifekit-hq.github.io/lifekit-common/ or `node_modules/@lifekit-hq/ui`. Use `cmn-button`, `cmn-input`, `cmn-form-field`, `cmn-alert`, `cmn-card`, etc. — never raw `<input>`, `<button>`, or `<div class="error">` when the library already has the component.

**Registry auth**: `@lifekit-hq/*` installs from GitHub Packages — `frontend/.npmrc` expects `NODE_AUTH_TOKEN` in the environment (`export NODE_AUTH_TOKEN=$(gh auth token)`, token needs `read:packages`). CI passes `secrets.GITHUB_TOKEN` (deploy no longer builds the frontend — it pulls the CI-built image; only the break-glass local build in `docker/deploy.sh` needs it); Docker builds take it as the `npm_token` BuildKit secret.

**Local iteration against unpublished library changes**: in `lifekit-common` run `npm run build && npm pack ./dist/lifekit-hq/ui`, then here `npm install <tarball>` (repeat per iteration; never commit a tarball reference). Day-to-day component development happens in lifekit-common's Storybook, not by running this app.

---

## File Organisation Rule (Frontend)

In Angular modules, each concept lives in its own file — no mixing:
- **Interfaces / types** → `models/<entity>/<entity>.model.ts` (or `*.types.ts` for type-alias-only files)
- **Domain constants** → `constants/<entity>/<entity>.constants.ts` (separate sibling tree to `models/`); page-only UI constants → `<page>.constants.ts` next to the page
- **Component class** → `*.component.ts` (no inline interface or constant definitions)
- **Service class** → `*.service.ts` (HTTP-only; no state, no inline interfaces — import from model files)
- **State** → `<feature>/store/*.state.ts` · `*.computed.ts` · `*.methods.ts` · `*.effects.ts` · `*.store.ts` (see State Management rule)
- **Validators** → `<feature>/validators/*.validator.ts`

**Shared rule**: any type, constant, utility, or enum used by more than one feature module MUST live in `frontend/src/app/shared/`. Never define cross-module concerns inside a feature folder. If a piece of code is imported by two or more modules, move it to `shared/` in the same PR.

This is a **hard gate** — inline interfaces in component files and cross-module code outside `shared/` block PR merge (see constitution Principle VI.5). Use `/frontend-code-quality` for an audit sweep.

---

## Frontend State Management — NgRx SignalStore

State belongs in a `signalStore()` under `modules/<feature>/store/`, **never** in component classes. Components are declarative: form definitions, template bindings, one-line dispatch handlers. No `ngOnInit` fetches, no `effect()` in components, no local `isLoading`/`errorMessage` fields.

**Mandatory file split** (per store):

| File | Role |
|---|---|
| `<name>.state.ts` | `interface <Name>State`, literal unions, `initial<Name>State` |
| `<name>.computed.ts` | `<name>Computed(store)` — pure derivations (`isLoading`, `errorMessage`, etc.) |
| `<name>.methods.ts` | `<name>Methods(store)` — synchronous `patchState` mutations only |
| `<name>.effects.ts` | `<name>Effects(store)` — `rxMethod`s for HTTP/async; `<name>Hooks(store)` for router subscriptions and signal effects |
| `<name>.store.ts` | `signalStore(..., withState(initial), withMethods(methods), withComputed(computed), withMethods(effects), withHooks({onInit: hooks}))` |

Rules:
- **Do not annotate return types on `*Methods`, `*Computed`, `*Effects` factories** — `withMethods` composition collapses explicit interfaces to `MethodsDictionary` and breaks `inject`. The `eslint.config.mjs` override for `**/store/**/*.ts` turns off `explicit-module-boundary-types` exactly for this reason.
- **App-wide stores** (e.g. `AuthStore`) use `{providedIn: 'root'}`. **Page-scoped stores** (e.g. `DashboardStore`) are provided on the component via `providers: [Store]` — they tear down with the route.
- **No `setInterval`.** Periodic refresh uses `timer(ms, ms).pipe(switchMap(...))` inside an `rxMethod` in `*.effects.ts`, kicked off by `onInit`.
- **No component subscriptions.** Components inject the store and bind `store.someSignal()` in templates. For flows, call `store.someMethod(payload)` and rely on computed signals for loading/error feedback.
- Unit tests live next to the files (`*.spec.ts`), use `TestBed.runInInjectionContext` and `signalState(initialState)` for lightweight fixtures. Run with `TZ=Europe/Dublin npx ng test --watch=false` (Vitest via `@angular/build:unit-test`); `npm test` / `npm run test:ci` already pin that zone, which timezone-sensitive specs (e.g. `TransactionGroupUtils`) need to fail before their fix.

---

## Type Unification — extract narrow shared bases as duplication appears

When ≥3 model interfaces share the same fields with identical types, extract a structural base into `shared/models/<base-name>/<base-name>.model.ts` and refactor consumers to `extends`. Currently in place: `AccountIdentity` (account identifier fields) and `Timestamped` (`createdAt`). The `frontend-type-unification` skill covers the audit + extract loop and the criteria for *refusing* to extract (type divergence, optional/required mismatch, n=2 duplication, etc.). Don't unify aggressively — duplication of two is fine, hiding legitimate type divergence behind a base is not.

---

## Utility Helpers — always a `*.utils.ts` class

Pure helper functions are NEVER bare `export function`s in a random file. Each helper lives in `<domain>.utils.ts` (e.g. `error.utils.ts`, `money.utils.ts`) under `frontend/src/app/shared/utils/` (cross-module) or `frontend/src/app/modules/<feature>/utils/` (feature-local), as a class with `public static` methods:

```ts
export class ErrorUtils {
  public static extractCode(err: unknown): Nullable<string> { ... }
}
```

Rules:
- One domain per file. `error.utils.ts` holds error helpers, `money.utils.ts` holds money helpers — never mix.
- Methods are `public static`, no instance state, no DI, no `inject()`. If you need DI, make it a service in `services/` instead.
- **Template-bound helpers must have a thin pipe wrapper.** If any `*.html` calls the helper, create `shared/pipes/<name>.pipe.ts` (or `modules/<feature>/pipes/`) whose `transform()` just delegates to the static method. Templates use the pipe; components don't expose the function via `public readonly fooFn = fooFn`.
- Every `*.utils.ts` ships with `<domain>.utils.spec.ts` (Vitest) — one branch per `it`, edge cases (null/undefined/empty), `vi.useFakeTimers()` for time-dependent helpers. Coverage on the util file: 100%.

The `frontend-utils-creation` skill covers the full mechanics; trigger it whenever you're tempted to write a bare helper function.

---

## Money Display — always `MoneyUtils` / `money` pipe

Every displayed amount goes through `MoneyUtils.format` (`shared/utils/money.utils.ts`; templates use the thin `money` pipe in `shared/pipes/`) — never `DecimalPipe` + a hand-appended currency code, and no per-module money pipes. Format: symbol-first (`$18,981.48`, `-€1,200.00`); ISO code prefix only where the symbol is ambiguous (`CHF 950.00`); `—` for null/NaN. A base-currency equivalent is shown muted on its own second line via `MoneyUtils.formatEquivalent` (`~ $1,235`). This is display only — conversion and aggregation rules stay in [`money-semantics.md`](../money-semantics.md).

---

## Custom Providers — always extract

Any provider beyond Angular's built-in `provideX()` helpers (`ErrorHandler`, custom injection tokens, `APP_INITIALIZER`, class-based `HTTP_INTERCEPTORS`, etc.) MUST be extracted to `frontend/src/app/core/providers/<name>.provider.ts`:

```ts
export function provideX(): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: TOKEN, useValue: ... }]);
}
```

`app.config.ts` then lists `provideX()` calls only. One provider concern per file. Feature-scoped providers live under `modules/<feature>/providers/`. The `angular-provider-extraction` skill enforces this.

---

## Error Message Resolution

Error-code → user-message mapping is centralized. **Do not** add an `if/else` ladder in a component or store.

- Mechanism lives in `@lifekit-hq/ui`: `ERROR_MESSAGES` injection token + `ErrorMessageService.resolve(code)` → `string | null`.
- App provides the registry: `src/app/core/errors/error-messages.registry.ts` holds the flat `Record<string, string>` covering all backend `errorCode` values. Wired via `provideErrorMessages()` in `app.config.ts`.
- Stores consume via `inject(ErrorMessageService)` inside `*.computed.ts`, falling back to a feature-specific default (`'Failed to load dashboard data.'`, `'Invalid email or password.'`, etc.) when `resolve()` returns `null`.
- **When adding a new error code on the backend:** append the message to the registry in the same PR. The `error?.errorCode` extraction helper stays local to `*.effects.ts` (the `extractErrorCode(err)` pattern).
- **Exception — server-composed messages:** `ACCOUNT_LOCKED` (login lockout, 429 + `Retry-After`) is deliberately not in the registry: its text carries the remaining wait, so `auth.computed.ts` shows the server's `error` text (`ErrorUtils.extractMessage`, kept in `AuthStore.errorDetail`) for that code only. Every other unregistered code keeps the flow fallback.

---

## Async State — `cmn-async-state`

A region that swaps between loading, error and content uses `cmn-async-state` from `@lifekit-hq/ui` (`[status]`, `[errorMessage]`, `[skeletonRows]`, `[skeletonHeight]`) instead of local `@if (...Loading())` / error branching. Adopters: `events` (upcoming list) and `truelayer-picker`.

The primitive renders uniform full-width skeleton bars, a bare error alert that replaces the content, and a plain-text empty line. Surfaces that need more keep local markup; each gap:

| Surface | Missing in the primitive |
|---|---|
| `holdings` | row-shaped skeleton (avatar + two text lines + trailing value) in a card; empty state is a card with a second "import pending" variant |
| `asset-dossier` | composite skeleton with per-block heights/widths (heading + three card-sized blocks) |
| `dashboard` | per-widget inline skeletons inside live cards (no region swap); icon + CTA empty state; error banner sits above content that stays rendered |
| `flow-breakdown` | multi-column row skeleton in a card; persistent error banner above content; empty state with message + sub-message that varies by range |
| `accounts-list` | multi-column row skeleton in a card; error alert with a Retry action; empty state with a CTA slot |
| `transaction-ledger` | row-shaped skeleton (avatar + two text lines + trailing value) in a card; persistent error banner; empty state that varies by active filter |

Adopt the primitive on these when it gains a skeleton template slot, an error action slot and a rich empty slot; drop the row from this table in the same PR.

---

## Mobile Keyboards — `inputmode` on numeric fields

Every `<input type="number">` / `<cmn-input type="number">` declares `inputmode` (`decimal` for money, `numeric` for whole counts); the template lint rule `fns/money-inputmode` (`frontend/eslint-rules/money-inputmode.mjs`) fails the build otherwise. Add `enterkeyhint` and a known `autocomplete` token where they apply. `cmn-input` forwards only `autocomplete` to its inner `<input>`, so `InputHintsDirective` (`shared/directives/input-hints.directive.ts`) copies the host's `inputmode` / `enterkeyhint` down — write them as static attributes on `cmn-input`, not bindings.
