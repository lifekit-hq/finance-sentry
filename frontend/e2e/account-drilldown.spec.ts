import {expect, test} from '@playwright/test';

const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: []},
  expiresAt: '2027-01-01T00:00:00Z',
};

const ACCOUNT = {
  accountId: 'acc-aib',
  provider: 'truelayer',
  category: 'banking',
  bankName: 'AIB',
  accountType: 'current',
  accountNumberLast4: '1111',
  currency: 'EUR',
  currentBalance: 120,
  balanceInBaseCurrency: 130,
  syncStatus: 'synced',
  lastSyncTimestamp: '2026-10-05T00:00:00Z',
};

const SUMMARY = {
  totalNetWorth: 130,
  baseCurrency: 'USD',
  appliedFilters: {},
  categories: [
    {
      category: 'banking',
      totalInBaseCurrency: 130,
      institutionCount: 1,
      institutions: [
        {
          institutionId: 'inst-aib',
          provider: 'truelayer',
          name: 'AIB',
          category: 'banking',
          totalInBaseCurrency: 130,
          syncStatus: 'synced',
          lastSyncTimestamp: '2026-10-05T00:00:00Z',
          lastSuccessfulSyncTimestamp: '2026-10-05T00:00:00Z',
          accounts: [ACCOUNT],
        },
      ],
    },
  ],
};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

test.describe('Accounts → ledger drill-down', () => {
  test('an account row opens the ledger with that account preselected', async ({page}) => {
    const requests: URLSearchParams[] = [];
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/wealth/summary**`, route => route.fulfill(json(SUMMARY)));
    await page.route(`${API}/categories`, route => route.fulfill(json([])));
    await page.route(`${API}/accounts`, route =>
      route.fulfill(
        json({accounts: [{accountId: 'acc-aib', bankName: 'AIB', accountNumberLast4: '1111'}]})
      )
    );
    await page.route(`${API}/accounts/transactions**`, route => {
      requests.push(new URL(route.request().url()).searchParams);
      return route.fulfill(json({items: [], totalCount: 0, hasMore: false}));
    });

    await page.goto('/accounts/list');
    await page.getByTestId('account-row').click();

    await expect(page).toHaveURL(/\/transactions\?account=acc-aib/);
    await expect
      .poll(() => requests[requests.length - 1]?.getAll('accountId'))
      .toEqual(['acc-aib']);
    await expect(page.getByTestId('clear-filters')).toBeVisible();
  });
});
