import {chmodSync, existsSync} from 'node:fs';
import {join} from 'node:path';

import {type Browser, expect, type Page, test} from '@playwright/test';

import {SESSION_DIR_ENV} from './global-setup';

// READ-ONLY smoke against the deployed stack. This suite hits the production
// database as the seeded smoke account (a Member with fake data, seeded by the
// API from its email) — it must never create, mutate, or delete anything, and
// must assert shape (headings, tables, health), never exact values.
//
// Production has password sign-in off: Logto is the only way in. The smoke signs in as a
// dedicated Logto user by driving the real Logto sign-in page; that user's verified email is
// the smoke account's email, so the first sign-in links the two. The credentials come only
// from the environment; there is no default. E2E_LIVE_BASE_URL must be the app's public
// origin (the OIDC redirect returns there; cookies set on another host would not follow).
const EMAIL = process.env['E2E_LIVE_LOGTO_EMAIL'];
const PASSWORD = process.env['E2E_LIVE_LOGTO_PASSWORD'];
const LOGTO_STEP_TIMEOUT_MS = 30_000;
const SIGNED_IN_TIMEOUT_MS = 30_000;
const SIGN_IN_SETUP_TIMEOUT_MS = 120_000;
// A freshly recreated api answers its first data requests slowly (cold start); the page's own data
// call, not a fixed heading wait, decides when the ledger has loaded.
const LEDGER_DATA_TIMEOUT_MS = 30_000;
const LEDGER_TEST_TIMEOUT_MS = 60_000;
const RESTORED_SESSION_TIMEOUT_MS = 15_000;
// The signed-in session, kept after every test in the run's private directory (global-setup.ts
// creates it and removes it when the run ends). A failed test makes Playwright retry the whole serial
// group in a fresh worker, which would otherwise sign in to Logto a second time (and, inside the
// API's anonymous rate budget, may be answered 429). Without the directory nothing is persisted.
const SESSION_DIR = process.env[SESSION_DIR_ENV];
const SESSION_FILE = SESSION_DIR ? join(SESSION_DIR, 'session.json') : undefined;
const OWNER_ONLY = 0o600;

async function login(page: Page): Promise<void> {
  if (!EMAIL || !PASSWORD) {
    throw new Error(
      'E2E_LIVE_LOGTO_EMAIL and E2E_LIVE_LOGTO_PASSWORD must both be set to sign in to the deployed stack.'
    );
  }
  // The unauthenticated '/' bounces to /login, which forwards straight to Logto when it is the
  // only sign-in method. Every full page load spends two requests (/auth/methods, /auth/me) of
  // the API's 10-per-minute anonymous budget per address, so do not reload around it.
  await page.goto('/');
  const identifier = page.locator('input[name="identifier"]');
  await identifier.waitFor({timeout: LOGTO_STEP_TIMEOUT_MS});
  await identifier.fill(EMAIL);
  await page.locator('input[name="password"]').fill(PASSWORD);
  // Logto shows its terms checkbox on the same form when the tenant requires agreement.
  const terms = page.locator('input[name="termsAgreement"]');
  if ((await terms.count()) > 0 && !(await terms.isChecked())) {
    await terms.check({force: true});
  }
  await page.getByRole('button', {name: /^sign in$/i}).click();
  // After the password step Logto offers to create a passkey (the tenant has passkey sign-in on);
  // skip it so the smoke never binds one. Otherwise Logto returns straight to the app, which lands
  // on the dashboard; the heading only exists in the app.
  const skipPasskey = page.getByRole('button', {name: /^skip$/i});
  const dashboard = page.getByRole('heading', {name: 'Dashboard'});
  await expect(skipPasskey.or(dashboard)).toBeVisible({timeout: SIGNED_IN_TIMEOUT_MS});
  if (await skipPasskey.isVisible()) {
    await skipPasskey.click();
  }
  await expect(dashboard).toBeVisible({timeout: SIGNED_IN_TIMEOUT_MS});
}

// The retry reuses the session the first attempt saved; only when that does not land on the dashboard
// (no file, expired cookies) does it fall back to a real sign-in.
async function openSession(browser: Browser): Promise<Page> {
  if (SESSION_FILE && existsSync(SESSION_FILE)) {
    const context = await browser.newContext({storageState: SESSION_FILE});
    const restored = await context.newPage();
    try {
      await restored.goto('/');
      await expect(restored.getByRole('heading', {name: 'Dashboard'})).toBeVisible({
        timeout: RESTORED_SESSION_TIMEOUT_MS,
      });
      return restored;
    } catch {
      await context.close();
    }
  }
  const page = await browser.newPage();
  await login(page);
  return page;
}

test.describe('Live smoke — deployed stack', () => {
  // One Logto sign-in per run, shared by the tests that need a session: a sign-in spends several
  // requests of the API's 10-per-minute anonymous budget per address, so a second one inside the
  // same minute is answered 429.
  test.describe.configure({mode: 'serial'});

  let page: Page;

  test.beforeAll(async ({browser}) => {
    test.setTimeout(SIGN_IN_SETUP_TIMEOUT_MS);
    page = await openSession(browser);
  });

  test.afterEach(async () => {
    if (SESSION_FILE) {
      await page.context().storageState({path: SESSION_FILE});
      chmodSync(SESSION_FILE, OWNER_ONLY);
    }
  });

  test.afterAll(async () => {
    await page.context().close();
  });

  test('API health endpoint responds ok through the gateway', async ({request}) => {
    const response = await request.get('/api/v1/health');
    expect(response.ok()).toBe(true);
  });

  test('login lands on a populated dashboard', async () => {
    // The smoke account has a seeded bank account — the empty state must not show.
    await expect(page.getByText('Connect your first account')).not.toBeVisible();
    await expect(page.getByText('Last 3 months')).toBeVisible();
    await expect(page.getByRole('button', {name: /view income details/i})).toBeVisible();
    await expect(page.getByRole('button', {name: /view spending details/i})).toBeVisible();
  });

  test('transaction ledger renders with live data', async () => {
    test.setTimeout(LEDGER_TEST_TIMEOUT_MS);
    // Only a successful response counts: the auth interceptor may retry a 401 after a token refresh.
    const ledgerData = page.waitForResponse(
      response =>
        response.request().method() === 'GET' &&
        new URL(response.url()).pathname.endsWith('/accounts/transactions') &&
        response.ok(),
      {timeout: LEDGER_DATA_TIMEOUT_MS}
    );
    await page.goto('/transactions');
    await ledgerData;
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();
    await expect(page.getByRole('searchbox', {name: 'Search transactions'})).toBeVisible();
  });
});
