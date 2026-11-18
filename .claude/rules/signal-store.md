---
paths:
  - "frontend/src/app/**/store/**/*.ts"
  - "frontend/src/app/**/*.component.ts"
---

# State management - NgRx SignalStore

**Why it exists:** state in component classes (`ngOnInit` fetches, `effect()` in components, local `isLoading`/`errorMessage`, `setInterval` polling) is untestable and leaks across routes; the five-file split keeps stores composable (`specs/045-net-worth-projection` cites it).
**Retire when:** an ESLint rule bans `effect()`/`setInterval`/`subscribe` in `*.component.ts` and requires the `*.state/computed/methods/effects/store` split.

State belongs in a `signalStore()` under `modules/<feature>/store/`, **never** in component classes. Components are declarative: form definitions, template bindings, one-line dispatch handlers. No `ngOnInit` fetches, no `effect()` in components, no local `isLoading`/`errorMessage` fields.

**Mandatory file split** (per store):

| File | Role |
|---|---|
| `<name>.state.ts` | `interface <Name>State`, literal unions, `initial<Name>State` |
| `<name>.computed.ts` | `<name>Computed(store)` - pure derivations (`isLoading`, `errorMessage`, etc.) |
| `<name>.methods.ts` | `<name>Methods(store)` - synchronous `patchState` mutations only |
| `<name>.effects.ts` | `<name>Effects(store)` - `rxMethod`s for HTTP/async; `<name>Hooks(store)` for router subscriptions and signal effects |
| `<name>.store.ts` | `signalStore(..., withState(initial), withMethods(methods), withComputed(computed), withMethods(effects), withHooks({onInit: hooks}))` |

- **Do not annotate return types on `*Methods`, `*Computed`, `*Effects` factories** - `withMethods` composition collapses explicit interfaces to `MethodsDictionary` and breaks `inject`. The `eslint.config.mjs` override for `**/store/**/*.ts` turns off `explicit-module-boundary-types` for this reason.
- **App-wide stores** (e.g. `AuthStore`) use `{providedIn: 'root'}`. **Page-scoped stores** (e.g. `DashboardStore`) are provided on the component via `providers: [Store]` - they tear down with the route.
- **No `setInterval`.** Periodic refresh uses `timer(ms, ms).pipe(switchMap(...))` inside an `rxMethod` in `*.effects.ts`, kicked off by `onInit`.
- **No component subscriptions.** Components inject the store and bind `store.someSignal()` in templates. For flows, call `store.someMethod(payload)` and rely on computed signals for loading/error feedback.
- Unit tests live next to the files (`*.spec.ts`), use `TestBed.runInInjectionContext` and `signalState(initialState)` for lightweight fixtures. Run with `TZ=Europe/Dublin npx ng test --watch=false` (Vitest via `@angular/build:unit-test`); `npm test` / `npm run test:ci` already pin that zone, which timezone-sensitive specs (e.g. `TransactionGroupUtils`) need to fail before their fix.
