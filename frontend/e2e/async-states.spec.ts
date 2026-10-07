import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: []},
  expiresAt: '2027-01-01T00:00:00Z',
};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

async function signIn(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
}

test.describe('cmn-async-state surfaces', () => {
  test('ledger: error shows a Retry that recovers into the empty state', async ({page}) => {
    await signIn(page);
    let fail = true;
    await page.route(`${API}/accounts/transactions**`, route =>
      fail
        ? route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
        : route.fulfill(json({items: [], totalCount: 0, hasMore: false}))
    );
    await page.goto('/transactions');

    const errorAlert = page.locator('cmn-errorAlert');
    await expect(errorAlert).toBeVisible();
    fail = false;
    await errorAlert.getByRole('button', {name: 'Retry'}).click();

    await expect(errorAlert).toHaveCount(0);
    await expect(page.getByText('No transactions found')).toBeVisible();
  });

  test('ledger: loading shows skeleton rows before the list', async ({page}) => {
    await signIn(page);
    await page.route(`${API}/accounts/transactions**`, async route => {
      await new Promise(resolve => setTimeout(resolve, 800));
      await route.fulfill(json({items: [], totalCount: 0, hasMore: false}));
    });
    await page.goto('/transactions');

    await expect(page.locator('cmn-skeleton').first()).toBeVisible();
    await expect(page.getByText('No transactions found')).toBeVisible();
  });
});
