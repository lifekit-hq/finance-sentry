import {expect, type Page, test} from '@playwright/test';

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
  // Logto returns to the app, which lands on the dashboard; the heading only exists in the app.
  await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible({
    timeout: SIGNED_IN_TIMEOUT_MS,
  });
}

test.describe('Live smoke — deployed stack', () => {
  test('API health endpoint responds ok through the gateway', async ({request}) => {
    const response = await request.get('/api/v1/health');
    expect(response.ok()).toBe(true);
  });

  test('login lands on a populated dashboard', async ({page}) => {
    await login(page);
    // The smoke account has a seeded bank account — the empty state must not show.
    await expect(page.getByText('Connect your first account')).not.toBeVisible();
    await expect(page.getByText('Last 3 months')).toBeVisible();
    await expect(page.getByRole('button', {name: /view income details/i})).toBeVisible();
    await expect(page.getByRole('button', {name: /view spending details/i})).toBeVisible();
  });

  test('transaction ledger renders with live data', async ({page}) => {
    await login(page);
    await page.goto('/transactions');
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();
    await expect(page.getByRole('searchbox', {name: 'Search transactions'})).toBeVisible();
  });
});
