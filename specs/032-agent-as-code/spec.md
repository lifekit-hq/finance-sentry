# Feature Specification: Agent-as-Code (Ledger definition lives in the repo)

**Feature Branch**: `032-agent-as-code`
**Created**: 2026-07-22
**Status**: Implemented
**Input**: User description: "Place the finance agent in the Finance Sentry repo and manage it as part of this project. Finance Sentry is the core; the agent is just an agent with tools. FS provides the data and tools; the agent's definition (persona, jobs, policy) should be version-controlled in the repo and deployed to the OpenClaw runtime by CI. The gateway/runtime itself stays external."

## Overview

Today the companion agent (Ledger) is defined by files hand-edited directly on the server (its persona/workspace files, its scheduled jobs, its per-agent config). That means the thing that *powers* the agent — its data and tools — lives in this repo, reviewed and versioned, while the agent's *definition* lives out-of-band on a box, undocumented and prone to drift. Every change is a manual server edit with no review, no history, and no way to reproduce.

This feature makes the **agent's definition a first-class, versioned part of the Finance Sentry repo**, and makes the external agent runtime a **deploy target rather than the source of truth**. The repo owns *what the agent is* (persona, the specs for its scheduled behaviors, and a manifest of the tools/data it depends on); the existing deployment pipeline syncs that definition to the runtime on merge. The agent runtime keeps owning *execution* (the model loop, channels, delivery) and its own secrets/wiring.

This is the structural change that lets "Finance Sentry is the core; the agent is just an agent with tools" actually hold: the agent's identity and operating rules evolve in the same reviewed repo as the data and tools it uses.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The agent's definition lives in the repo (Priority: P1)

The agent's definition — its persona/identity, its tool-usage notes, and the declarative specs for its scheduled behaviors — exists as reviewed files inside the Finance Sentry repository, alongside the backend and the tool (MCP) surface it uses. A change to how the agent behaves is a pull request, not a server edit: it has an author, a diff, a review, and history.

**Why this priority**: This is the foundational move and the whole point — making the repo the single source of truth for the agent. It delivers value even before any automated deploy exists: the definition is captured, reviewable, and reproducible instead of living only on a box.

**Independent Test**: Inspect the repo and confirm it contains a complete, human-readable definition of the agent (persona + scheduled-behavior specs + a manifest of required tools/data) sufficient to recreate the agent's configuration on a fresh runtime. Confirm a behavior change can be proposed and reviewed as a normal PR.

**Acceptance Scenarios**:

1. **Given** the repo, **When** a reviewer opens the agent-definition directory, **Then** they find the agent's persona, its scheduled-behavior specs, and a manifest of the tools/data it depends on — enough to understand and reproduce the agent.
2. **Given** a desired change to the agent's behavior, **When** it is made, **Then** it is made as a change to repo files (a reviewable diff with history), not an ad-hoc server edit.
3. **Given** the definition in the repo, **When** compared to what the running agent actually uses, **Then** the two describe the same agent (the repo is authoritative).

---

### User Story 2 - CI deploys the definition to the runtime on merge (Priority: P2)

When a change to the agent definition is merged, the existing deployment pipeline syncs it to the agent runtime and applies it (including any runtime reload needed), so the running agent reflects the repo without anyone hand-editing the server.

**Why this priority**: Turns the versioned definition into the live behavior automatically, closing the drift gap. Depends on US1 (there must be a repo definition to deploy).

**Independent Test**: Change a persona/definition file, open and merge a PR, and observe the running agent reflect the change after the pipeline runs — with no manual server edit.

**Acceptance Scenarios**:

1. **Given** a merged change to the agent definition, **When** the pipeline runs, **Then** the runtime is updated to match the repo and reloaded as needed, with no manual step.
2. **Given** a scheduled-behavior spec added/changed/removed in the repo, **When** the pipeline runs, **Then** the runtime's scheduled jobs are reconciled to match the repo (added/updated/removed accordingly).
3. **Given** a deploy that fails partway, **When** it errors, **Then** it fails loudly and leaves the prior working definition in place rather than a half-applied state.
4. **Given** the definition references a secret (e.g. a delivery destination or credential), **When** it is deployed, **Then** the secret is resolved from the runtime's own secret store by reference — secrets are never stored in the repo.

---

### User Story 3 - Drift between repo and runtime is visible (Priority: P3)

If the running agent is hand-edited out-of-band so it no longer matches the repo, the pipeline surfaces the divergence (and, per policy, reconciles it back to the repo) so the repo stays authoritative and silent drift can't accumulate.

**Why this priority**: Protects the "repo is the source of truth" guarantee over time. Valuable hardening, but the core value is delivered by US1+US2.

**Independent Test**: Hand-edit a definition on the runtime so it differs from the repo, run the pipeline/check, and confirm the divergence is reported (and reconciled per the chosen policy).

**Acceptance Scenarios**:

1. **Given** a runtime hand-edited to differ from the repo, **When** the drift check runs, **Then** the divergence is reported clearly (what differs).
2. **Given** the reconcile policy, **When** the pipeline runs, **Then** the runtime is brought back to match the repo (repo wins) unless the change is explicitly exempted.

---

### Edge Cases

- **Secrets**: a definition needs a credential or a delivery destination → referenced by name and resolved at deploy from the runtime's secret store; never committed.
- **Runtime-only settings**: model routing, tool allow-lists, channel/account wiring that are genuinely runtime concerns → remain owned by the runtime config, not force-fit into the repo (the repo owns the agent's *content*, not the gateway's wiring).
- **Bad definition merged**: a definition that would break the agent → the deploy validates before applying and fails safe, keeping the last-good definition live.
- **Manual emergency edit on the box**: an urgent hand-fix during an incident → allowed, but surfaced as drift so it gets back-ported to the repo rather than silently persisting.
- **Multiple agents on the same runtime**: this feature governs only the finance agent's definition; other agents on the same runtime are untouched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The repository MUST contain a complete, human-readable definition of the finance agent: its persona/identity, its tool-usage notes, and declarative specs for its scheduled behaviors (name, schedule, purpose, target, delivery intent).
- **FR-002**: The repository MUST include a manifest of the tools/data the agent depends on (e.g. which MCP tool surface and data it operates against), so the definition is self-describing.
- **FR-003**: Changes to the agent definition MUST be made as reviewable repository changes (PR with diff and history), not ad-hoc runtime edits, and this MUST be the documented path.
- **FR-004**: The deployment pipeline MUST sync the repo's agent definition to the runtime on merge and apply it (including any runtime reload required) with no manual server edit.
- **FR-005**: The pipeline MUST reconcile the runtime's scheduled jobs to the repo's specs — creating, updating, and removing jobs so the runtime matches the repo.
- **FR-006**: Secrets MUST NOT be stored in the repo. Any secret a definition needs MUST be referenced by name and resolved from the runtime's secret store at deploy time.
- **FR-007**: The pipeline MUST validate a definition before applying it and MUST fail safe (leave the prior working definition live) on validation or deploy failure.
- **FR-008**: The system MUST make drift between the repo definition and the running agent visible, and MUST support reconciling the runtime back to the repo (repo is authoritative) per policy.
- **FR-009**: The feature MUST NOT absorb the agent runtime itself (model loop, channels, delivery, gateway) into the repo; those remain external. The repo owns the agent's *content + policy*, the runtime owns *execution + wiring*.
- **FR-010**: The feature MUST touch only the finance agent's definition; other agents sharing the runtime MUST be unaffected.

### Key Entities *(include if feature involves data)*

- **Agent Definition Bundle**: the repo-owned set describing the agent — persona/identity files, tool-usage notes, scheduled-behavior specs, and the dependency manifest. Source of truth.
- **Scheduled-Behavior Spec**: a declarative description of one recurring agent behavior (name, schedule, purpose, target/session, delivery intent) that the pipeline reconciles into a runtime job.
- **Deploy/Reconcile Run**: one application of the bundle to the runtime — validate, sync content, reconcile jobs, reload — with a success/failure outcome.
- **Drift Report**: the difference between the repo bundle and the running agent at a point in time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A newcomer can read the repo and reconstruct the finance agent's full configuration on a fresh runtime using only the repo definition (no undocumented server state required).
- **SC-002**: **100%** of intended agent-behavior changes land via reviewed repository changes rather than ad-hoc runtime edits (the documented, enforced path).
- **SC-003**: A merged agent-definition change is reflected in the running agent by the pipeline with **zero** manual server edits.
- **SC-004**: **Zero** secrets appear in the repository; every secret a definition needs is resolved by reference at deploy.
- **SC-005**: A failed deploy never leaves the agent in a half-applied state — the last-good definition remains live **100%** of the time on failure.
- **SC-006**: Drift introduced by an out-of-band edit is detected and reported on the next pipeline run **every** time.

## Assumptions

- **Existing pipeline is the vehicle**: the deployment automation that already ships Finance Sentry to the runtime host is extended to also sync the agent definition — no new, separate CI system is introduced.
- **Repo owns content, not wiring**: the initial scope is the agent's persona/workspace files, scheduled-behavior specs, and dependency manifest. Runtime-only wiring (model routing, tool allow-lists, channel/account, secrets) stays in the runtime config and is referenced, not owned. This split is tunable but is the starting boundary.
- **Single primary agent**: the finance agent (Ledger) is the target; the pattern may generalize to other agents later but that is not in scope here.
- **Runtime reload is available**: the runtime supports reloading an agent's definition/jobs without a full teardown; the pipeline uses that.
- **Reconcile policy default**: repo-wins for managed content; genuine emergency hand-edits are surfaced as drift to be back-ported rather than silently kept.

## Notes

- **[DECISION] Repo is the source of truth; runtime is a deploy target**: the agent's definition is versioned in Finance Sentry and applied to the external runtime by CI. This is the structural enabler for the "FS is the core, the agent is a thin consumer" architecture — the agent's identity and rules live beside the data and tools it uses.
- **[DECISION] Content vs. wiring boundary**: repo owns persona + scheduled-behavior specs + dependency manifest; the runtime keeps the model loop, channels, delivery, secrets, and low-level wiring. Rationale: absorbing the runtime into the repo would fight the tool and pull secrets/infra into the app repo.
- **[OUT OF SCOPE] The notification policy + event system**: modes and event-driven push are Finance Sentry feature 031, not this feature. 032 is purely about *where the agent definition lives and how it deploys*.
- **[OUT OF SCOPE] The agent runtime/gateway itself**: not migrated into the repo.
- **[DEFERRED] Generalizing to other agents**: the same GitOps pattern could later own other agents' definitions; deferred until the finance agent proves it out.
- **[DEPENDS ON] Deploy infrastructure**: relies on the existing self-hosted deployment pipeline having (or being granted) the access to write the agent's runtime definition and trigger a reload.
