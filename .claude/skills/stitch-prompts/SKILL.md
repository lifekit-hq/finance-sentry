---
name: stitch-prompts
description: Generate Stitch (Google Labs UI design tool) prompts for Finance Sentry screens and execute them directly via MCP. Use when the user wants to design, redesign, or mock up a page with Stitch — or asks for "stitch prompts", "UI prompts", or help with the Stitch workspace. Drives Stitch end-to-end: crafts prompt → sends to Stitch via MCP → pulls result back → ready for Angular implementation.
user-invocable: true
allowed-tools:
  - Read
  - Write
  - Bash(ls *)
  - Bash(find *)
  - Bash(grep *)
  - mcp__stitch__list_projects
  - mcp__stitch__get_project
  - mcp__stitch__list_screens
  - mcp__stitch__get_screen
  - mcp__stitch__generate_screen_from_text
  - mcp__stitch__edit_screens
  - mcp__stitch__generate_variants
  - mcp__stitch__list_design_systems
  - mcp__stitch__apply_design_system
---

# Stitch MCP Driver — Finance Sentry

Design Finance Sentry screens end-to-end via the Stitch MCP: craft prompt → send to Stitch → pull result → hand off to Angular implementation. Follow Stitch's [official prompt guide](https://discuss.ai.google.dev/t/stitch-prompt-guide/83844) — prompts must be plain conversational language, short, and scoped to one change at a time.

## Active Stitch project

**Finance Sentry Wealth Dashboard**
- Project ID: `2377738634696453555`
- Design system: `assets/c71e9082d1d14e9dbcc6796dbb1f0ba3` (Finance Sentry Design System, v1)
- Device type: `DESKTOP`

Always target this project. Do not use the older "Finance Sentry" project (`11537407583059591048`).

## Prompt rules (non-negotiable)

1. **Plain English, not markdown specs.** No bullet lists inside the prompt body, no XML tags, no JSON. Short paragraphs Stitch can parse.
2. **One screen per `generate_screen_from_text` call. One change per `edit_screens` call.** Never bundle "add a filter bar AND change the table AND add a drawer" — Stitch will re-layout and drop elements.
3. **Keep each prompt under ~1,500 characters.** Stitch starts omitting components past ~5,000; stay well below.
4. **Zoom-Out → Zoom-In.** For a new screen, write a broad layout paragraph first. Refinements come after.
5. **Reference-by-location when refining.** Start `edit_screens` prompts with where the change lands: *"On the dashboard, in the hero row, …"*.
6. **Use vibe adjectives.** Finance Sentry's vibe: *minimal, institutional, data-dense, numbers-as-hero, low-chrome.* Never "playful", "vibrant", or "consumer".

## Finance Sentry design context (source of truth for prompts)

- **App**: Personal finance aggregator. Banks (TrueLayer, Monobank), crypto (Binance), brokerage (IBKR). Single user, power tool.
- **Vibe**: Institutional ops dashboard, not a consumer app. Data-dense. Numbers are the hero.
- **Color**: Indigo primary (`#4F46E5`) on primary actions and key numbers. Everything else neutral grays. Status: green `#10B981` = gains/synced, red `#EF4444` = losses/errors, amber `#F59E0B` = stale/warning.
- **Type**: Inter throughout. Tabular monospaced numerals for all money, right-aligned.
- **Surfaces**: 1px neutral border over drop shadows. Cards = 12px radius + border, flat. Inputs/buttons = 8px radius. No shadows.
- **Icons**: Lucide, 20px, stroke 1.75.
- **Shell** (authenticated pages): 240px left sidebar (Dashboard, Accounts, Transactions, Holdings, Settings; collapsible to 64px). Top bar: title left, ⌘K search center, theme toggle + avatar right. Main maxes at 1440px with 32px padding.
- **Responsive**: 1440 desktop, 768 tablet, 375 mobile.

## Current screens (pages that exist or are planned)

Verify via `find frontend/src/app/modules -type d -name pages` before claiming coverage:

- Auth: login, register
- Bank-sync: accounts-list, connect-account, transaction-ledger, dashboard
- Not yet scaffolded but planned: holdings (crypto + brokerage), settings, global shell

## Workflow when invoked

1. **Clarify intent** if args are ambiguous:
   - Generate a new screen from scratch?
   - Refine/edit an existing Stitch screen?
   - Generate variants of an existing screen?

2. **Check codebase** for up-to-date context (`find`, `grep`) — component names, existing page list. Do not assume.

3. **Craft the prompt** following the rules above.

4. **Execute via MCP**:
   - New screen → `mcp__stitch__generate_screen_from_text` (projectId: `2377738634696453555`, deviceType: `DESKTOP`)
   - Edit existing → `mcp__stitch__edit_screens` (pass `selectedScreenIds`)
   - Variants → `mcp__stitch__generate_variants`

5. **Pull the result** with `mcp__stitch__get_screen` and report back: screen name, dimensions, and a summary of what Stitch produced. If `output_components` in the response contains suggestions, present them to the user.

6. **Hand off**: Note the screen ID for Angular implementation. The implementation should reference the Stitch design as the visual spec.

## Prompt templates

### Template — New screen prompt

Short paragraph, 3–6 sentences. State the page name, top-level layout, and primary elements by position. Leave micro-details for follow-up `edit_screens` calls.

Good:
```
Design the dashboard as the main landing screen inside the authenticated shell. 
Four KPI cards across the top: Total Wealth with a sparkline and month-over-month 
delta, then Banks, Crypto, Brokerage. Below that, a 12-month net worth line chart 
taking two-thirds of the width and an allocation donut taking one-third. Below the 
charts, a Recent Activity card with the last 10 transactions in a compact table.
```

Bad (too dense — bundles too much):
```
Design the dashboard with 4 KPI cards, 2 charts, a recent activity table with 
48px rows, a sync status panel, an empty state, a slide-out drawer, hover states, 
loading skeletons, and a mobile breakdown.
```

### Template — Refinement prompt (edit_screens)

One sentence. Start with location. One change only.

```
On the dashboard, in the hero row, make the dollar amounts larger and use tabular 
monospaced numerals.
```

```
On the accounts page, add a filter pill row under the title with All, Banks, 
Crypto, Brokerage options.
```

## Anti-patterns to avoid in generated prompts

- ❌ Markdown headers inside the prompt body.
- ❌ Bullet lists inside the prompt body.
- ❌ Pixel-perfect measurements for every element.
- ❌ "Also add…", "And then…", multi-clause changes.
- ❌ Component library jargon (`cmn-*`) — prefer visual descriptions.
- ❌ Re-explaining theming per screen — the design system handles it.

## Final check before calling MCP

- Prompt is under ~1,500 characters.
- Each edit/refinement contains exactly one change.
- Vibe language is institutional, not playful.
- Targeting project `2377738634696453555`, deviceType `DESKTOP`.
