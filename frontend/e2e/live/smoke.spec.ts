import {expect, type Page, test} from '@playwright/test';

// READ-ONLY smoke against the deployed stack. This suite hits the production
// database as the QA test user — it must never create, mutate, or delete
// anything, and must assert shape (headings, tables, health), never exact
// values, which depend on live data.
const EMAIL = process.env['E2E_LIVE_EMAIL'] ?? 'test@gmail.com';
const PASSWORD = process.env['E2E_LIVE_PASSWORD'] ?? 'Darkfly21';

async function login(page: Page): Promise<void> {
  await page.goto('/');
  // The cmn-input wrapper mirrors the placeholder attribute of its inner native
  // input, so placeholder/role locators match twice — target the native inputs.
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.locator('input[type="password"]').fill(PASSWORD);
  await page.getByRole('button', {name: /authenticate/i}).click();
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
    // The QA test user has connected accounts — the empty state must not show.
    await expect(page.getByText('Connect your first account')).not.toBeVisible();
    await expect(page.getByText('Income (MTD)')).toBeVisible();
    await expect(page.getByText('Spending (MTD)')).toBeVisible();
  });

  test('transaction ledger renders with live data', async ({page}) => {
    await login(page);
    await page.goto('/transactions');
    await expect(page.getByRole('heading', {name: 'Transactions', exact: true})).toBeVisible();
    await expect(page.getByPlaceholder('Search transactions')).toBeVisible();
  });
});
