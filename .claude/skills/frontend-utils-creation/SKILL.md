---
name: frontend-utils-creation
description: Use when creating a new helper/utility function on the Angular frontend (a pure function that isn't UI, not state, not HTTP, not Angular DI-bound). Enforces the *.utils.ts class-with-static-methods pattern, automatic pipe wrapping for template-bound helpers, and Vitest spec coverage. Trigger on phrases like "add a util", "extract this into a helper", "I need a helper for X", or whenever you're about to write a bare `export function` under `shared/` or `modules/<feature>/`.
allowed-tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Bash(cd frontend && npx eslint *)
  - Bash(cd frontend && npx ng test *)
  - Bash(ls *)
---

# Frontend Utility Creation — Finance Sentry

Bare `export function`s in random files are NOT how utilities are created in this project. Every helper follows the same shape so consumers, tests, and future-you know exactly where to look.

## When this applies

Trigger on **any** new pure helper that is:
- Domain-agnostic logic (formatting, parsing, transformation, lookup)
- A function that operates on inputs and returns outputs with no side effects
- Code you're about to inline in a component, service, store, or effects file because "it's just a small thing"

**Does not apply to:**
- Angular services (`@Injectable`) — those go in `services/`
- Stateful logic — that belongs in a SignalStore under `<feature>/store/`
- Pure Angular pipes that don't reuse a util — those can stay in `pipes/` directly
- Type guards / type predicates — those live next to the types in `models/*.types.ts`

## What to produce

### 1. Always: a `*.utils.ts` file with a static-method class

```ts
// shared/utils/<domain>.utils.ts  (or modules/<feature>/utils/<domain>.utils.ts)

export class <Domain>Utils {
  public static <verbName>(input: T): U {
    // pure logic, no side effects
  }
}
```

Rules:
- File name: `<domain>.utils.ts`. The `<domain>` is a noun (`error`, `time`, `currency`), not a verb. Singular.
- Class name: `<Domain>Utils` (PascalCase). One class per file.
- Methods: `public static`, named with imperative verbs (`extractMessage`, `toIsoDate`, `formatBalance`).
- No instance state. No constructor. No DI. No `inject()`.
- No private instance fields — module-level `const`s for magic numbers (named, no inline literals).
- Methods are pure: same input → same output, no clocks/randomness/IO unless the method *is* about that (e.g. `DateRangeUtils.fromRelativeRange` defaults `now` to `new Date()` — that's the point of the method).
- Multiple related helpers → group as static methods on the same class. Different domains → different files.

### 2. If the helper is consumed in a template: also create a pipe

If any consumer references the util from an `*.html` file or string-interpolated template, wrap it in a pipe:

```ts
// shared/pipes/<name>.pipe.ts  (or modules/<feature>/pipes/<name>.pipe.ts)

import {Pipe, type PipeTransform} from '@angular/core';

import {<Domain>Utils} from '../utils/<domain>.utils';

@Pipe({name: '<camelCaseName>'})
export class <Name>Pipe implements PipeTransform {
  public transform(value: T, ...args: A[]): U {
    return <Domain>Utils.<verbName>(value, ...args);
  }
}
```

The pipe is a thin shell — the logic lives in the util, not in the pipe. Templates use `{{ value | <camelCaseName> }}`. Components stop having `public readonly fooHelper = fooHelper;` boilerplate.

### 3. Always: a Vitest spec

`<domain>.utils.spec.ts` next to the util.

```ts
import {describe, expect, it} from 'vitest';

import {<Domain>Utils} from './<domain>.utils';

describe('<Domain>Utils.<verbName>', () => {
  it('returns the expected output for a typical input', () => {
    expect(<Domain>Utils.<verbName>(input)).toBe(expected);
  });

  it('handles null/undefined/empty edge cases', () => {
    expect(<Domain>Utils.<verbName>(null)).toBe(<edge result>);
  });

  // ...one `it` per branch in the implementation
});
```

Coverage requirements:
- Every branch covered (each `if`, `??`, `?:`, ternary, switch case)
- Every documented edge case (null, undefined, empty string, empty array, zero, negative)
- For time/randomness/external dependencies: use `vi.useFakeTimers()` / `vi.setSystemTime()` to keep tests deterministic

## Where to put utils

| Scope | Location |
|---|---|
| Used by 2+ feature modules OR by `shared/` itself | `frontend/src/app/shared/utils/<domain>.utils.ts` |
| Used inside a single feature only | `frontend/src/app/modules/<feature>/utils/<domain>.utils.ts` |

If a util starts feature-local and gets reached for from a second module, **move it to `shared/utils/` in the same PR** (per the file-organization rule).

## Common patterns

### Utility name → class name → method

| Concept | File | Class | Method |
|---|---|---|---|
| Resolve a relative range to dates | `date-range.utils.ts` | `DateRangeUtils` | `fromRelativeRange(range)` |
| Pull the server message from an HTTP error body | `error.utils.ts` | `ErrorUtils` | `extractMessage(err)` |
| Capitalize / titlecase strings | `string.utils.ts` | `StringUtils` | `capitalize(s)`, `truncate(s, max)` |
| Currency math (no formatting — use a pipe for that) | `currency.utils.ts` | `CurrencyUtils` | `convertToBase(amount, rate)` |

Relative timestamps ("5m ago") are not a local util: use `formatRelativeTime` from `@lifekit-hq/core/format`, or the `cmnRelativeTime` pipe from `@lifekit-hq/ui` in templates.

### Anti-patterns to refuse

- `export function someHelper(...)` at top level of a non-`*.utils.ts` file. Refuse and extract.
- `*.utils.ts` containing several unrelated helpers. Split by domain.
- Pipe with logic inlined in `transform()` and no util backing it. Extract to a util the pipe calls.
- Util with `inject()` or `@Injectable()`. Convert to a class with static methods, or — if it really needs DI — make it a service in `services/`, not a util.
- Util without a spec. Reject the PR.

## After producing the util

Run from `frontend/`:
1. `npx eslint <new files> --fix`
2. `npx ng test finance-sentry` — confirm the new spec passes and coverage for the util file is 100%

## Migration of legacy bare-function utils

If you encounter an existing `shared/utils/<name>.ts` that just `export function`s, refactor it the same PR you touch it:
1. Rename `<name>.ts` → `<domain>.utils.ts`
2. Wrap the functions as `public static` methods on `<Domain>Utils`
3. Update every importer (`extractMessage` → `ErrorUtils.extractMessage`, etc.)
4. If template usage exists, add the pipe and remove the `public readonly someFn = someFn` boilerplate
5. Add the spec if missing
