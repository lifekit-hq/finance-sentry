# Ledger — agent as code

This directory is the **canonical source of truth** for the Ledger finance agent's persona. It exists so the persona is versioned, reviewable, and consumed by *every* runtime from one place — instead of being hand-edited on a server.

Feature: [`specs/040-in-app-finance-agent`](../../specs/040-in-app-finance-agent/spec.md). Realizes the "**FS is core, agent is thin**" direction — the substance (IPS, risk, holdings, radar/regime, theses) lives in Finance Sentry and is read via tools; the persona is a thin instruction layer over it.

## Layout

```
agent/ledger/
├── persona.core.md        # runtime-agnostic: identity, expertise, operating laws, tone, tool philosophy
├── user.md                # who Ledger serves (shared context, referenced by the core)
├── adapters/
│   ├── openclaw.md        # OpenClaw overlay: Kit, build-ask handoff via Kit, A2A, vault, state files, cron cadence, Telegram-via-Kit
│   └── browser.md         # Finance Sentry web-app overlay: direct chat, in-app tools, no orchestrator/cron
└── README.md
```

## Composition model

A runtime's effective persona = **`persona.core.md` + exactly one adapter**.

- **OpenClaw Ledger** (Telegram + proactive/scheduled briefs) = `persona.core.md` + `adapters/openclaw.md`.
- **Browser Ledger** (interactive, in the FS web app) = `persona.core.md` + `adapters/browser.md`.

Both surfaces are the **same brain** — same identity, same finance discipline, same guardrails — differing only in delivery, orchestration, and cadence. Change a shared law (e.g. the stay-invested rule, the tier-3 line) **once in the core**, and both surfaces get it. Never duplicate a core rule into an adapter.

## Invariants (what review enforces)

1. **Core is runtime-agnostic.** No Kit/DevClaw/Telegram/session/cron/file-path mechanics in `persona.core.md` — those belong in an adapter.
2. **No hard-coded policy.** The core never writes literal IPS targets, risk caps, or allocation numbers; it directs the agent to read them from the live tools (`get_ips`, `get_risk_rules`, `get_allocation_vs_target`) at answer time.
3. **OpenClaw equivalence.** `core + adapters/openclaw.md` is behaviorally equivalent to the persona the live OpenClaw Ledger runs on — no operating law, guardrail, or tool-use rule lost in the split. (This is the migration safety property, SC-002.)
4. **OpenClaw size budget.** `core + adapters/openclaw.md` must stay ≤ 20,000 characters (OpenClaw's `agents.defaults.bootstrapMaxChars`; a longer AGENTS.md is truncated live). The tool section names tools and when to reach for them — the MCP server describes arguments and returns at runtime. Pinned by `PersonaParityTests.OpenClawCompose_StaysWithinTheBootstrapLimit`.

## Coexistence

The OpenClaw Ledger and the browser Ledger **coexist** and share this core. OpenClaw keeps owning Telegram and proactive/scheduled work; the browser agent is the interactive surface. Neither replaces the other.

## Deploy

The live OpenClaw Ledger persona is now composed and deployed *from* this directory:
- **Source:** `persona.core.md` + `adapters/openclaw.md`, pinned in lifekit-stack's [`scripts/ledger-persona/PIN`](https://github.com/lifekit-hq/lifekit-stack/blob/main/scripts/ledger-persona/PIN).
- **Installation:** an operator runs lifekit-stack's `ledger-persona.py install` (no deploy script runs it). It reads the pin, fetches `persona.core.md` and `adapters/openclaw.md` at that commit through the GitHub contents API (`gh`'s existing login, no checkout), joins them, and writes the result to the live workspace's `AGENTS.md`, keeping the previous file as `AGENTS.md.pre-persona-install`. It refuses a composed persona over 20,000 characters (invariant 4).
- **Drift detection:** A daily check (enabled by lifekit-stack PR #326) alerts if the live persona diverges from the pinned repo version.
- **Update workflow:** Persona changes land as PRs here, merge, then the pin is bumped in lifekit-stack (closing the hand-edit loop; feature #040 / #416).
