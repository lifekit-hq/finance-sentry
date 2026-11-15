# Feature Specification: Thesis revisions - Ledger proposes, the owner confirms

**Feature Branch**: `fm/fs-ledger-promote-propose-confirm`

**Created**: 2026-11-15

**Status**: Implemented (backend + MCP + the SPA confirm surface)

**Origin**: Finance flow scout (firstmate `data/fs-finance-flow-scout/report.md` §0 Q1 option b, §5 P1/P2/P6), chosen by the
owner on 2026-11-15. It **narrows** PR #1383 (2026-11-13, "Ledger writes theses directly"): promotion and every thesis
write return to the owner; Ledger keeps writing candidates (`reject_candidate`, `score_candidate`) and the watchlist, and
gains a server-side pending artifact for the rest. #1383's `origin` stamp and audit trail stay.

## Context

| Concern | Before | Now |
|---|---|---|
| `save_thesis`, `promote_candidate`, `revert_thesis_revision` | open to Ledger's service token | `RequireMcpPersonalToken` (owner session only) |
| Ledger forming or changing a thesis | direct write | `propose_thesis_revision` -> pending `ThesisRevision` |
| Confirm | none | `decide_thesis_revision` (owner-only MCP) / `POST research/thesis-revisions/{id}/confirm\|reject` (`ai.use`) |
| Evidence at entry (P1) | shipped in #1380 | unchanged; a proposal's `Proposed` content overrides the scorecard defaults field by field |

## User stories

1. **Propose a promotion (P1).** Ledger has walked the promote ritual on an Active candidate. It calls
   `propose_thesis_revision` kind `Promote` (optional text, data points, catalysts, triggers, falsifiers, entry price,
   `proposedUsd`, `paper`) with a rationale and evidence refs. Nothing is promoted.
2. **Propose a change to a held thesis.** Kinds `NarrativeUpdate` (text, data points, catalysts, falsifiers, entry price),
   `TriggerChange` (invalidation triggers, closed vocabulary) and `Close`.
3. **Confirm is the single write.** The owner's confirm runs the existing commands as the owner's own write: `Promote` ->
   `PromoteCandidateCommand` (risk gate first; a refusal returns `RiskGateRefused`, leaves the proposal pending, and an
   override is the owner's explicit flag), `NarrativeUpdate`/`TriggerChange` -> `SaveThesisCommand` (journals `Revised` with
   prior and new version, the proposal's rationale and evidence), `Close` -> delete + `Closed` journal row.
4. **Reject keeps the reason** as the counterfactual; **expiry** (14 days, max 30) and **supersede** (one open proposal per
   subject) settle every proposal; rows are never deleted.

## Requirements

- **FR-001** A service token MUST NOT be able to call `save_thesis`, `promote_candidate`, `revert_thesis_revision` or
  `decide_thesis_revision`: hidden from `tools/list`, refused on call (`ResearchWriteAuthorizationTests`).
- **FR-002** `DecideThesisRevisionCommandHandler` MUST refuse any actor other than `OwnerSession`/`McpPersonal` (service
  token, in-app agent, fleet agent, job) before reading the proposal, so the boundary does not rest on a transport attribute.
- **FR-003** A proposal MUST be validated at propose time with the rules a direct save applies (trigger vocabulary,
  falsifier bounds, text length, kind/content shape, active candidate, existing thesis, an actual change).
- **FR-004** Confirm MUST NOT place or change a position (recommend-only wall unchanged).
- **FR-005** One migration: additive table `research.thesis_revisions` (M030); no existing row is read, rewritten or backfilled.
- **FR-006** `thesis-revision-expiry` (daily 05:20 UTC) expires pending proposals past their date.

## SPA confirm surface (P6)

- **FR-007** The Decisions queue (`/ledger/decisions`) lists each pending revision as a card: before and after text, the
  trigger diff (added, removed, kept), the evidence links, and Confirm / Reject (reject needs a reason). The last 30 days'
  outcomes follow, with the reason a change was turned down.
- **FR-008** The asset dossier shows the ticker's pending revision under the hero, as it does a recommendation, and says what
  was recorded once decided; a confirm writes the resulting thesis onto the page.
- **FR-009** A promotion the risk gate refuses (409) leaves the proposal pending, shows the refusal and offers the owner's
  explicit override; every other failure resolves through the error-code registry.
- **FR-010** `GET research/thesis-revisions` additively carries `evidenceLinks` per row (a stored news article resolves to its
  title and URL; filing, event and fundamentals refs carry a label only). No behaviour or authorization changed.
- The surface reads each card's "before" from `GET research/theses`; a failed read leaves the card without one.

## Out of scope / follow-ups

- P5: the pulse step and persona wiring in lifekit-stack (operator step; the persona core already names the tools).
- The in-app Ledger reaches every MCP tool in the signed-in person's scope (`McpToolBridge`), so it can still call
  `save_thesis`/`promote_candidate` as `User`. It cannot confirm (FR-002). Closing that is a separate, owner-visible change.
- P11: `Origin` on `InvestmentThesis` from the proposal's `ProposedBy` (a confirmed write is stamped `User`; the
  rationale text names the proposer).
