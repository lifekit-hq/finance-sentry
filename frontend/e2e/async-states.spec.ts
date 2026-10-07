import {expect, type Page, test} from '@playwright/test';

import {WEALTH_SUMMARY} from './support/dossier-mocks';

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

const EMPTY_DASHBOARD = {
  aggregatedBalance: {},
  totalNetWorthUsd: 0,
  accountCount: 0,
  accountsByType: {},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
  baseCurrency: 'USD',
};

const LEDGER_ROW = {
  transactionId: 't-1',
  accountId: 'a-1',
  bankName: 'Monzo',
  currency: 'USD',
  amount: -12.5,
  amountUsd: -12.5,
  date: '2026-10-06T09:00:00Z',
  postedDate: '2026-10-06T09:00:00Z',
  description: 'Coffee',
  transactionType: 'debit',
  merchantCategory: 'dining',
  isPending: false,
  createdAt: '2026-10-06T09:00:00Z',
};

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

    const errorAlert = page.locator('cmn-async-state cmn-alert');
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

  test('dashboard: an error is never the empty state, and Retry recovers', async ({page}) => {
    await signIn(page);
    let fail = true;
    await page.route(`${API}/dashboard/aggregated**`, route =>
      fail
        ? route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
        : route.fulfill(json(EMPTY_DASHBOARD))
    );
    await page.route(`${API}/net-worth/history**`, route =>
      route.fulfill(json({snapshots: [], hasHistory: false}))
    );
    await page.goto('/dashboard');

    const errorAlert = page.locator('cmn-async-state cmn-alert');
    await expect(errorAlert).toBeVisible();
    await expect(page.getByText('Connect your first account')).toHaveCount(0);
    fail = false;
    await errorAlert.getByRole('button', {name: 'Retry'}).click();

    await expect(errorAlert).toHaveCount(0);
    await expect(page.getByText('Connect your first account')).toBeVisible();
  });

  test('accounts: an error shows Retry, and Retry loads the accounts', async ({page}) => {
    await signIn(page);
    let fail = true;
    await page.route(`${API}/wealth/summary`, route =>
      fail
        ? route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
        : route.fulfill(json(WEALTH_SUMMARY))
    );
    await page.goto('/accounts/list');

    const errorAlert = page.locator('cmn-async-state cmn-alert');
    await expect(errorAlert).toBeVisible();
    await expect(page.getByText('No accounts connected yet.')).toHaveCount(0);
    fail = false;
    await errorAlert.getByRole('button', {name: 'Retry'}).click();

    await expect(errorAlert).toHaveCount(0);
    await expect(page.locator('cmn-disclosure-row').first()).toBeVisible();
  });

  test('ledger: going offline keeps the rows and says when they were last synced', async ({
    page,
    context,
  }) => {
    await signIn(page);
    await page.route(`${API}/accounts/transactions**`, route =>
      route.fulfill(json({items: [LEDGER_ROW], totalCount: 1, hasMore: false}))
    );
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);

    await context.setOffline(true);
    const notice = page.getByTestId('offline-notice');
    await expect(notice).toContainText("You're offline");
    await expect(notice).toContainText('last sync');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await expect(page.locator('cmn-async-state cmn-alert')).toHaveCount(0);

    await context.setOffline(false);
    await expect(notice).toHaveCount(0);
  });

  test('login: a failed auth/methods call shows Retry and keeps OIDC reachable', async ({page}) => {
    const unauthorized = (route: {fulfill: (o: {status: number}) => Promise<void>}) =>
      route.fulfill({status: 401});
    await page.route(`${API}/auth/me`, unauthorized);
    await page.route(`${API}/auth/refresh`, unauthorized);
    let fail = true;
    await page.route(`${API}/auth/methods`, route =>
      fail
        ? route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
        : route.fulfill(json({oidc: false, passwordLogin: true}))
    );
    await page.goto('/login');

    const error = page.getByTestId('methods-error');
    await expect(error).toBeVisible();
    await expect(page.getByRole('button', {name: 'Sign in', exact: true})).toBeVisible();
    await expect(page.getByLabel('Email address')).toHaveCount(0);
    fail = false;
    await error.getByRole('button', {name: 'Retry'}).click();

    await expect(error).toHaveCount(0);
    await expect(page.getByLabel('Email address')).toBeVisible();
  });
});
