---
paths:
  - "frontend/src/app/**/*.ts"
---

# Frontend file organisation, helpers and providers

**Why it exists:** merged from four concerns that share one reason - each is a structural convention a linter *could* check but does not yet (see the PR's ESLint follow-up candidates), and each applies to the same files. Hard gate: constitution Principle VI.5 (inline interfaces and cross-module code outside `shared/` block PR merge).
**Retire when:** each section is replaced by its own ESLint rule (`*.utils.ts` class shape, provider extraction, shared-boundary); delete the section as its rule lands, the file when empty.

## Decorators

Do **not** add `standalone: true` to `@Component` / `@Pipe` / `@Directive`: it is the default and dead boilerplate (`prefer-standalone` only flags `standalone: false`, so this is not linted).

## Cross-module code

The file-per-concept layout (interfaces and types in `models/`, constants in `constants/`, no inline interface, enum or exported constant in a `*.component.ts` / `*.service.ts`) is enforced by the `structure/file-per-concept` ESLint rule. What a linter cannot check: any type, constant, utility, or enum used by more than one feature module MUST live in `frontend/src/app/shared/`. If code is imported by two or more modules, move it to `shared/` in the same PR. Use `/frontend-code-quality` for an audit sweep.

## Utility helpers

The `*.utils.ts` shape (a class of `public static` methods, no bare exported functions or arrow constants, a sibling `*.utils.spec.ts`) is enforced by the `utils/utils-class` ESLint rule. What a linter cannot check: helpers live under `shared/utils/` (cross-module) or `modules/<feature>/utils/` (feature-local), one domain per file, no `inject()` (needs DI → a service). **Template-bound helpers need a thin pipe wrapper** (`shared/pipes/<name>.pipe.ts` or `modules/<feature>/pipes/`) whose `transform()` delegates to the static method; components don't expose the function via `public readonly fooFn = fooFn`. The `frontend-utils-creation` skill covers the mechanics.

## Custom providers - always extract

Any provider beyond Angular's built-in `provideX()` helpers (`ErrorHandler`, custom tokens, `APP_INITIALIZER`, class-based `HTTP_INTERCEPTORS`) is extracted to `frontend/src/app/core/providers/<name>.provider.ts` as `export function provideX(): EnvironmentProviders { return makeEnvironmentProviders([...]); }`. `app.config.ts` lists `provideX()` calls only; one concern per file; feature-scoped providers live under `modules/<feature>/providers/`. The `angular-provider-extraction` skill enforces this.

## Type unification

When ≥3 model interfaces share the same fields with identical types, extract a structural base into `shared/models/<base-name>/<base-name>.model.ts` and `extends` it (in place: `AccountIdentity`, `Timestamped`). Don't unify aggressively: duplication of two is fine; never hide legitimate type divergence behind a base. The `frontend-type-unification` skill covers the audit.
