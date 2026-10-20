---
name: frontend-type-unification
description: Use when creating or editing TypeScript model interfaces on the Angular frontend, or when the user asks to "unify these types", "extract a base interface", "reduce typing duplication", or notes that "these entities share fields". Audits for duplicated fields across model files, recommends narrow shared base interfaces (no deep hierarchies), and refactors entities to extend them. Trigger proactively whenever you're about to add a new `*.model.ts` file or notice three or more fields repeated across two or more interfaces.
allowed-tools:
  - Read
  - Write
  - Edit
  - Glob
  - Grep
  - Bash(cd frontend && npx tsc *)
  - Bash(cd frontend && npx eslint *)
  - Bash(cd frontend && npx ng test *)
  - Bash(find *)
  - Bash(grep *)
---

# Frontend Type Unification — Finance Sentry

When you create or modify model interfaces, audit for repeated fields across the codebase. Extract narrow shared bases ONLY when the fields are genuinely the same concept AND have the same type. The goal is **honest deduplication, not aesthetic uniformity**.

## When this applies

Trigger on:
- Creating a new `*.model.ts` file (audit before writing — does this entity overlap with existing ones?)
- Editing an interface and adding fields that look familiar (`accountId`, `createdAt`, `id`, `currency`, etc.)
- The user explicitly asking ("unify", "extract base", "reduce duplication")
- After an entity-folder migration that surfaces a cluster of similar models

## Existing shared bases

Always check these first — extending an existing base is preferable to inventing a new one:

| Base interface | Fields | Located in |
|---|---|---|
| `AccountIdentity` | `accountId`, `bankName`, `accountType`, `accountNumberLast4`, `currency` | `shared/models/account-identity/account-identity.model.ts` |
| `Timestamped` | `createdAt: string` | `shared/models/timestamped/timestamped.model.ts` |

Imported from the library, never redeclared: `Nullable<T>` (`@lifekit-hq/ui`), `AsyncStatus` (`@lifekit-hq/core`).

## The audit loop

### 1. Detect duplication

```bash
# Count repeated field declarations across all model files
find frontend/src/app -name "*.model.ts" -print0 \
  | xargs -0 grep -hE "^\s+(\w+):" \
  | sort | uniq -c | sort -rn | head -30
```

Anything with **count ≥ 3** AND **identical type** is a candidate. Counts ≥ 5 are nearly always worth extracting.

### 2. Decide: extract or skip

Extract a base interface ONLY IF:
- ≥3 entities share the same field set (not just one field — three matters because that's what makes a "shape")
- The field types are **identical** (not `string` in one and `Nullable<string>` in another)
- The fields represent the same domain concept (don't unify `accountId: string` on a `BankAccount` with `accountId: string` on an audit-log row that happens to have the same name)

**Refuse to extract** when:
- Only 2 entities share fields and they're closely related siblings — duplication is fine for n=2
- Types disagree (`syncStatus: string` vs `syncStatus: SyncStatus`) — the divergence is intentional, hiding it forces ugly casts later
- Optional vs required differs (`provider: string` vs `provider?: string`) — `Partial<>` makes the relationship unclear
- The "common" fields are coincidentally named but mean different things in each entity

When refusing, **say so explicitly** in the report — don't silently leave the duplication.

### 3. Extract the base

If extraction is justified:

1. Create the entity folder under `shared/models/<base-name>/<base-name>.model.ts` (per the entity-folder rule):

   ```ts
   export interface <BaseName> {
     // only fields that are identical across all consumers
   }
   ```

2. Naming: PascalCase, descriptive, **structural** noun (`AccountIdentity`, `Timestamped`, `Owned`), not generic (`BaseEntity`, `Common`, `Shared`).

3. Refactor each consumer to `extends <BaseName>` and **delete** the now-redundant own fields. Use multi-extend (`extends A, B`) when an entity composes from two bases.

4. Verify:
   ```bash
   npx tsc --noEmit -p tsconfig.app.json
   npx ng test finance-sentry
   ```

### 4. Don't go deeper than two levels

`A extends B extends C` is a smell. If you find yourself wanting a base for the base, stop and ask: are these really one concept, or two? Prefer **composition via multi-extend** over a hierarchy.

## Anti-patterns to refuse

- **Generic catch-all bases**: `interface BaseEntity { id: string; createdAt: string; updatedAt: string }` applied to everything. The `id` field is rarely the same shape (`accountId`/`transactionId`/`userId`); generic bases blur the domain.
- **`Partial<Base>` to widen optionality**: if some entities don't have a field, that field doesn't belong in the base. Split the base, don't paper over it.
- **`Pick<Base, K>` / `Omit<Base, K>` chains**: when you have to start picking pieces of a base, the base is wrong-shaped.
- **Renaming during extract**: if extracting forces you to rename a field (`bank_name` → `bankName` because the base demands camelCase but one entity has snake_case), the base isn't unifying — it's transforming. Fix the inconsistent entity in a separate, focused PR first.
- **Unifying API contract types and domain types**: a backend-DTO interface (`AuthResponse`) and a domain entity (`User`) often share fields by accident. Don't fuse them — DTOs are wire shapes, entities are runtime shapes.

## Reporting

When you spot duplication but decide NOT to extract, mention it briefly so the user knows you saw it:

> Noticed `provider: string` (3 entities) vs `provider: Nullable<string>` (1 entity). Skipping unification — the type divergence is real (one comes from the wealth endpoint, the other from the bank-account endpoint). If you want them aligned, fix at the source.

When you DO extract, report:
- Base name + fields
- Which entities now extend it
- Which entities you intentionally left alone, and why

## Reminder for new model files

Before writing a new `*.model.ts`:
1. List the fields you're about to declare.
2. For each field, run a quick grep: `grep -rn "^\s*<fieldName>:" frontend/src/app/**/*.model.ts`.
3. If a field already exists ≥2 places with the same type, your new entity is the third — extract NOW (or extend an existing base if one fits).
