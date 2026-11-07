# Finance Sentry — QA Guide

## QA — Test Account

`test@gmail.com` is no longer a real account; it remains only as a stubbed identity in the route-mocked Playwright specs. Production has password sign-in off; the live checks sign in through Logto as a dedicated Logto smoke user whose credentials live only in the CI secrets `E2E_LIVE_LOGTO_EMAIL` / `E2E_LIVE_LOGTO_PASSWORD` (its email is also the seeded, passwordless smoke account's email).

### Key test scenarios (check before declaring any fix done)

| Page | Golden path | Key assertions |
|---|---|---|
| **Login** | Click Sign in (forwards to Logto) | Redirects to `/dashboard` (or the originally requested route); no JS errors |
| **Accounts** | Load page | Banking/Brokerage/Digital Assets tables render; totalConnections > 0; Net worth shown |
| **Dashboard** | Load page | Total Balance ≠ $0.00 (if accounts exist); category table shows human-readable labels (not `FOOD_AND_DRINK`) |
| **Transactions** | Load page | Transaction rows render; categories human-readable; no spinner stuck |
| **Holdings** | Load page | Summary cards have labels; breakdown table has data |
| **Connect (TrueLayer)** | Click "Connect Account" → select Open Banking | Modal opens; provider list loads without 422/500 |
| **Disconnect** | Click Disconnect on any account | Confirmation dialog opens; account removed on confirm |

---

## QA — End-to-End Testing After Implementation

After **all tasks in a feature are complete**, act as a QA engineer: spin up the app and test the feature through the browser using Playwright MCP.

**Steps (mandatory):**
1. Ensure the full Docker stack is running: `cd docker && docker compose -f docker-compose.dev.yml up -d`
2. Wait for health check: `GET http://localhost:5001/api/v1/health` → `{"status":"healthy"}`
3. Open `http://localhost:4200` via Playwright
4. Navigate the golden path of the feature as a real user would — click buttons, fill forms, follow redirects
5. Also test key error/edge cases (invalid input, cancelled flows, etc.)
6. **Phone smoke (390 px)** for every UI change: resize the viewport to 390×844 and take one screenshot of each page the change touched, in light and dark. Check: the bottom tab bar (Home, Accounts, Transactions, Alerts, More) replaces the sidebar, nothing scrolls horizontally (`document.documentElement.scrollWidth === 390`), tables with list slots render as list rows, and drawers open as bottom sheets. Repeat at desktop width (≥ 1280 px) to confirm the sidebar layout is unchanged.
7. Report findings: what passed, what failed, screenshots of any broken state
8. If bugs are found, fix them (via Qwen) before declaring the feature done

**Tools:** Use `mcp__plugin_playwright_playwright__browser_*` tools — snapshot first, screenshot only when visual proof is needed.

---

## Screen gates (lifekit-common 4.4)

Three shared gates run over every routed screen from `frontend/e2e/screens/`, on route-mocked fake data only (no finance data reaches a screenshot): the **screen check** (composition rules C0–C10), the **route scan** (design judge + axe-core) and **pixel baselines** (390/1280, light/dark, committed Linux images). Run them with `npm run test:screens` / `npm run test:routes` in `frontend/` after `npm run build`; Frontend CI runs them in the `frontend-screens` job.

- `routes.ts` is the route list: each routed screen with the page kind (`cmn-page` kind) it is meant to be built on and its `mode`. `routes.spec.ts` fails when `AppRoute` gains a screen the list lacks.
- `mode: 'report'` prints and annotates findings and passes; `'enforce'` fails on them; `'pending'` has no fixtures yet. A route moves to `enforce` in the PR that adopts its page template and clears its findings. Never fix a layout finding with an app-local patch or exception: it is fixed once in lifekit-common.
- Baselines are Linux-only and exact. Never commit locally rendered ones: run the **Update screen baselines** workflow (Actions tab, or add the `update-baselines` label to the PR) on the branch, which renders them on the CI runner, commits them and posts the before/after on the PR. `.github/CODEOWNERS` asks the owner to review any `*-linux.png`.
- The older `*-visual.spec.ts` baselines are untouched; they share their fixtures with the gates through `e2e/support/screens/`.
