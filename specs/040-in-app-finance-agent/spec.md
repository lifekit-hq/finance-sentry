# Feature Specification: In-app finance agent (Ledger in FS)

**Feature Branch**: `040-in-app-finance-agent`
**Created**: 2026-08-08
**Status**: US1 shipped · US2 + US3 implemented (browser Ledger + parity)
**Input**: User description: "Bring the Ledger finance agent into Finance Sentry as canonical persona-as-code (runtime-agnostic core + OpenClaw and browser adapters), plus a Phase-2 in-browser interactive agent over the existing finance tool surface; coexists with the OpenClaw Ledger sharing one persona core."

## Overview

Today "Ledger" — Denys's finance specialist agent — exists only inside OpenClaw: its persona is hand-edited on a VPS, and Denys can only talk to it through Telegram (routed via the Kit orchestrator). Finance Sentry already owns the substance the agent reasons over — accounts, holdings, IPS, risk rules, allocation, radar/regime signals, theses, track record — exposed through a large finance tool surface. The agent is the thin layer; the data and tools are the core.

This feature brings the agent **into Finance Sentry**. First it makes the **persona a versioned artifact in the FS repo** (the single source of truth both runtimes read). Then it adds an **interactive agent in the FS web app**, so Denys can ask Ledger finance questions in his browser and get grounded, tool-backed answers in the same voice and with the same discipline as the Telegram Ledger — without depending on OpenClaw for that conversation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persona as versioned code, one source of truth (Priority: P1)

The Ledger persona lives in the Finance Sentry repository as a set of reviewable files: a **runtime-agnostic core** (who Ledger is, its finance-domain expertise, and its operating laws — materiality/silence-by-default, cite-the-reasoning-chain with source and threshold, three-layer market answers, stay-invested default, bias-to-inaction, tone, and tool-use philosophy) plus **runtime adapters** that layer on only what a specific runtime needs (the OpenClaw adapter: Kit/DevClaw hierarchy, agent-to-agent envelope, session messaging, Telegram-via-Kit; the browser adapter: talk directly to Denys in the web app, no orchestrator routing). The core references live Finance Sentry data through the existing tools and never hard-codes policy values (IPS targets, risk caps) that already live in the app.

**Why this priority**: The shared core is the foundation everything else builds on. It ends the "hand-edit a file on the VPS, hope the backup repo isn't lying" workflow, makes persona changes reviewable, and is the single artifact both the Telegram and browser agents consume. It delivers value on its own — a governed, diff-able persona — even before the browser agent exists.

**Independent Test**: A reviewer can open the repo, read the persona core and each adapter as separate files, confirm the core carries no runtime-specific instructions and no hard-coded policy numbers, and confirm that composing `core + openclaw-adapter` reproduces the persona the OpenClaw Ledger runs on.

**Acceptance Scenarios**:

1. **Given** the persona lives in the repo, **When** a reviewer inspects the core file, **Then** it contains identity, expertise, and operating laws only — no Kit/DevClaw/Telegram/session mechanics and no literal IPS/risk numbers.
2. **Given** the decomposed persona, **When** the OpenClaw adapter is composed onto the core, **Then** the result is behaviorally equivalent to the current live OpenClaw Ledger persona (same laws, same tool philosophy, same voice).
3. **Given** a change is needed to a shared operating law (e.g. the stay-invested rule), **When** it is edited once in the core, **Then** both the OpenClaw and browser compositions reflect it — the law is not duplicated per runtime.
4. **Given** the persona references a policy value, **When** the value is needed at runtime, **Then** the persona instructs the agent to read it from the live tool (IPS / risk rules), not from a number written in the persona.

---

### User Story 2 - Ask Ledger in the browser (Priority: P2)

Denys opens Finance Sentry, goes to a "Ledger" conversation surface, and asks a finance question in natural language ("why is my book down today?", "am I over my position cap on NVDA?", "what's the regime?"). Ledger answers as itself — grounded in his actual accounts, holdings, IPS, risk rules, and radar/regime signals via the existing finance tools — in the same disciplined voice as the Telegram Ledger, and shows which facts came from which tools.

**Why this priority**: This is the headline user value — Ledger available where Denys already reads his finances, without Telegram or the OpenClaw round-trip. It depends on US1's core existing but is a distinct, independently demonstrable slice.

**Independent Test**: Logged in as the test user, open the Ledger surface, ask a question that requires live data (e.g. allocation drift), and verify the answer reflects that user's real book, cites the source of each claim, and matches Ledger's established tone and discipline.

**Acceptance Scenarios**:

1. **Given** Denys is authenticated, **When** he asks a question that needs his portfolio, **Then** Ledger answers using his real holdings/accounts (his-scoped data) and attributes facts to their sources.
2. **Given** a market question ("what happened today?"), **When** Ledger answers, **Then** it follows the three-layer discipline (what moved → where money rotated → what it implies for his book/IPS) rather than repeating a headline.
3. **Given** a long-running answer that gathers data from several tools, **When** Ledger is working, **Then** Denys sees responsive, progressive feedback rather than a frozen screen.
4. **Given** a question outside the finance domain, **When** asked, **Then** Ledger declines gracefully and stays in its lane.
5. **Given** the conversation, **When** Denys asks a follow-up, **Then** Ledger retains the thread's context within the session.

---

### User Story 3 - One brain, two front-ends (coexistence & parity) (Priority: P3)

The OpenClaw Ledger (Telegram + proactive/scheduled briefs) and the new browser Ledger run the **same persona core**. Denys gets a consistent Ledger — same values, same discipline, same tone — regardless of surface. The OpenClaw runtime keeps working unchanged; the repo persona can later become the thing OpenClaw is deployed from.

**Why this priority**: Coexistence is the agreed model (no migration, no regression to the Telegram experience). It matters but is verified largely by US1's shared core plus not breaking OpenClaw; hence lowest of the three.

**Independent Test**: Pose the same finance question to the Telegram Ledger and the browser Ledger; the substance, discipline, and voice are consistent (allowing for surface differences like Telegram brevity vs. an interactive follow-up). Confirm the OpenClaw Ledger continues to operate after this feature ships.

**Acceptance Scenarios**:

1. **Given** both surfaces exist, **When** the same question is asked on each, **Then** the answers are consistent in policy, discipline, and voice.
2. **Given** this feature ships, **When** the OpenClaw Ledger runs its Telegram and proactive flows, **Then** they work unchanged (no capability removed from OpenClaw).
3. **Given** a persona-core edit, **When** it lands in the repo, **Then** both surfaces can pick it up from the same source (no per-runtime re-authoring).

### Edge Cases

- **Guardrail (money/credentials/trades)**: Denys asks Ledger to move money, place/modify a trade, change account state, or expose/rotate a credential. Ledger MUST NOT execute — it drafts and escalates for explicit confirmation (read + draft + escalate), identical to the Telegram tier-3 rule. Reads, analysis, drafting, and surfacing anomalies proceed without gating.
- **Stale / missing data**: a tool reports stale bars, a failed sync, or an unavailable data axis (e.g. regime rates axis `available:false`). Ledger says so and distrusts the affected numbers rather than inventing a value.
- **Tool error / timeout**: a finance tool errors mid-answer. Ledger degrades gracefully — reports what it could not retrieve, answers with what it has, does not fabricate.
- **Unauthenticated / other user**: the browser agent only ever operates on the logged-in user's data; it cannot be steered to read another user's book.
- **Prompt-injection via fetched content**: content the agent reads (news, filings) attempts to issue instructions. Ledger treats fetched content as data, not commands, and never lets it override its guardrails.
- **Persona drift**: a runtime-specific instruction is mistakenly added to the core. This is catchable in review because core and adapters are separate files.
- **Long/expensive question**: an open-ended request could fan out to many tools. Ledger stays proportional (bias-to-inaction / tool-proportionality) rather than running an unbounded sweep.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Ledger persona MUST live in the Finance Sentry repository as versioned, reviewable files, decomposed into a runtime-agnostic **core** and one **adapter per runtime** (OpenClaw, browser).
- **FR-002**: The persona **core** MUST contain only runtime-agnostic content (identity, finance-domain expertise, operating laws, tone, tool-use philosophy) and MUST NOT contain runtime-specific mechanics or hard-coded policy values that already exist as live data in the app.
- **FR-003**: A shared operating law or expertise statement MUST be defined once in the core and consumed by every runtime — never duplicated per adapter.
- **FR-004**: Composing the core with the OpenClaw adapter MUST reproduce a persona behaviorally equivalent to the current live OpenClaw Ledger (so OpenClaw can adopt the repo persona without regression).
- **FR-005**: The persona MUST direct the agent to obtain policy and portfolio facts (IPS, risk caps, allocation, holdings, regime, etc.) from the live finance tools at answer time, not from static text.
- **FR-006**: Finance Sentry MUST provide an interactive conversational surface in the web app where an authenticated user can ask finance questions in natural language and receive answers.
- **FR-007**: The browser agent MUST reason over the existing Finance Sentry finance tool surface (the same tools the OpenClaw Ledger uses) to ground its answers.
- **FR-008**: The browser agent MUST operate strictly within the **logged-in user's** data scope; it MUST NOT access or be steerable toward another user's data.
- **FR-009**: The browser agent MUST answer in Ledger's established voice and discipline (materiality, cite-the-reasoning-chain, three-layer market answers, stay-invested default, bias-to-inaction, tone) as defined by the shared core.
- **FR-010**: The browser agent MUST attribute factual claims to their source tool/data, so Denys can see where a number came from.
- **FR-011**: The browser agent MUST NOT execute money-movement, trade, account-state, or credential actions; for such requests it MUST draft and escalate for explicit confirmation (tier-3 guardrail), even where a capable tool exists.
- **FR-012**: The browser agent MUST provide responsive, progressive feedback during multi-step answers (no indefinitely frozen UI).
- **FR-013**: The browser agent MUST retain conversational context within a session and support follow-up questions.
- **FR-014**: The browser agent MUST degrade gracefully on stale data, missing data, or tool errors — reporting the gap and never fabricating values.
- **FR-015**: Shipping this feature MUST NOT remove or regress any existing OpenClaw Ledger capability (Telegram, proactive/scheduled briefs); the two coexist and share the core.
- **FR-016**: Fetched external content (news, filings, web) MUST be treated as data, never as instructions that can override the agent's guardrails or policy.

### Key Entities *(include if feature involves data)*

- **Persona Core**: the runtime-agnostic definition of Ledger — identity, expertise, operating laws, tone, tool-use philosophy. Single source of truth.
- **Runtime Adapter**: a runtime-specific overlay composed onto the core (OpenClaw adapter, browser adapter). Adds only what that runtime needs.
- **Conversation**: an interactive session between Denys and the browser agent — an ordered exchange of user questions and agent answers, scoped to the logged-in user, retaining context within the session.
- **Tool Surface**: the existing set of finance tools/data the agent reads to ground answers (accounts, holdings, IPS, risk rules, allocation, radar/regime, theses, track record, etc.) — reused, not redefined.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The persona core exists in the repo as the single source of truth, with zero duplicated operating laws across adapters and zero hard-coded policy values that duplicate live app data (verifiable by inspection).
- **SC-002**: Composing core + OpenClaw adapter reproduces the current live Ledger persona with no loss of any operating law, guardrail, or tool-use rule.
- **SC-003**: An authenticated user can ask a finance question in the browser and receive a grounded answer reflecting their **real** book, with every factual claim attributed to a source, in at least 95% of well-formed finance questions during acceptance testing.
- **SC-004**: For requests that would move money/credentials/trades, the browser agent escalates for confirmation instead of executing in 100% of test cases (zero unauthorized actions).
- **SC-005**: The same finance question posed to the Telegram Ledger and the browser Ledger yields answers consistent in policy, discipline, and voice (side-by-side reviewer judgment across a fixed question set).
- **SC-006**: After this feature ships, all existing OpenClaw Ledger golden paths (Telegram Q&A, proactive briefs) continue to work — zero regressions.
- **SC-007**: The browser agent begins responding (first visible progress) quickly enough that the surface never appears frozen during multi-tool answers.

## Assumptions

- **Existing tool surface is reused as-is.** The agent grounds itself in the finance tools/data Finance Sentry already exposes; this feature does not add new finance data sources. (Feature 035 already refined that tool surface.)
- **Authentication is reused.** The browser agent runs in the context of the already-authenticated Finance Sentry user; tools are already user-scoped. No new auth model.
- **Runtime, model, and streaming mechanics are deferred to planning.** The spec fixes requirements (grounded, disciplined, guarded, responsive, coexisting); `speckit.plan` selects how the browser agent is run.
- **Coexistence, not replacement.** The OpenClaw Ledger remains the Telegram + proactive surface; the browser agent is the interactive surface. Both consume the shared core.
- **Read-first scope for the browser agent.** Consistent with the Telegram Ledger, the browser agent is read/analyze/draft by default; any mutating/irreversible action is drafted and escalated, never executed autonomously.
- **The live OpenClaw persona is the reference** for the core+OpenClaw-adapter equivalence check (the current trimmed ~18k persona, plus the just-added market-regime tool).

## Notes

- [DECISION] **Coexistence model**: OpenClaw Ledger keeps Telegram + proactive/scheduled briefs; the FS browser agent is the interactive surface; both consume the same persona core. Rationale: lowest risk, no migration, preserves the Telegram experience while adding the browser surface. (Chosen by Denys.)
- [DECISION] **Spec-first on runtime**: the in-browser agent's runtime/model/streaming approach is intentionally left to `speckit.plan`. Rationale: capture the requirements (grounded, disciplined, guarded, responsive) without prematurely committing an implementation. (Chosen by Denys.)
- [DECISION] **Persona is thin; FS is the core.** Policy substance (IPS, risk caps, allocation) is live data reached via tools; the persona references it, never hard-codes it. Rationale: the "FS is core, agent is thin" direction and single-source-of-truth for policy.
- [OUT OF SCOPE] **Embeddings / research-RAG activation (036)**: turning on semantic corpus retrieval is deferred; the agent uses the existing tool surface without it.
- [OUT OF SCOPE] **Replacing the OpenClaw Ledger**: not a goal of this feature; the two coexist.
- [OUT OF SCOPE] **Autonomous mutating actions**: moving money, trading, changing account state, rotating credentials — the agent drafts and escalates; it never executes these. This guardrail is not waived by this feature.
- [DONE] **Agent-as-code deploy to OpenClaw (032)**: OpenClaw's persona is now installed *from* this repo at a lifekit-stack pin, closing the VPS→repo hand-edit loop (#416); see `agent/ledger/README.md` § Deploy.
- [DEFERRED] **Proactive/scheduled behavior in the FS-native runtime**: the browser agent is interactive/on-demand; proactive briefs remain OpenClaw's job for now.
