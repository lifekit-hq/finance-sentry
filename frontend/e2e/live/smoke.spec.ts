import {expect, type Page, test} from '@playwright/test';

// READ-ONLY smoke against the deployed stack. This suite hits the production
// database as the seeded smoke account (a Member with fake data, seeded by the
// API from the same credentials) — it must never create, mutate, or delete
// anything, and must assert shape (headings, tables, health), never exact
// values. The credentials come only from the environment; there is no default.
const EMAIL = process.env['E2E_LIVE_EMAIL'];
const PASSWORD = process.env['E2E_LIVE_PASSWORD'];

async function login(page: Page): Promise<void> {
  if (!EMAIL || !PASSWORD) {
    throw new Error(
      'E2E_LIVE_EMAIL and E2E_LIVE_PASSWORD must both be set to sign in to the deployed stack.'
    );
  }
  await page.goto('/');
  // The cmn-input wrapper mirrors the placeholder attribute of its inner native
  // input, so placeholder/role locators match twice — target the native inputs.
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.locator('input[type="password"]').fill(PASSWORD);
  await page.getByRole('button', {name: 'Sign in', exact: true}).click();
  // Post-login the app lands on /accounts (its default target); navigate from there.
  await page.waitForURL(url => !url.pathname.startsWith('/login'), {timeout: 15_000});
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', {name: 'Dashboard'})).toBeVisible({timeout: 15_000});
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
    await expect(page.getByText('This month')).toBeVisible();
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
