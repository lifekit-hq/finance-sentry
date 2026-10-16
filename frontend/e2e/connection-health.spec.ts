import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

// Connection-health surface (slice S5): the Retrying / Reconnect needed / Dormant labels a policy-driven provider's
// rows carry, the reconnect deep link a confirmed credential loss's alert opens, and the one Dormant notice.

const PHONE = {width: 390, height: 844};
const SYNCED_AT = new Date().toISOString();
const FROZEN_SINCE = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000).toISOString();

function account(
  id: string,
  bankName: string,
  last4: string,
  provider: string,
  balance: number,
  syncStatus: string
): Record<string, unknown> {
  return {
    accountId: id,
    bankName,
    accountType: 'current',
    accountNumberLast4: last4,
    currency: 'USD',
    provider,
    category: 'banking',
    currentBalance: balance,
    balanceInBaseCurrency: balance,
    syncStatus,
    lastSyncTimestamp: SYNCED_AT,
  };
}

function institution(
  id: string,
  provider: string,
  name: string,
  category: 'banking' | 'brokerage',
  total: number,
  syncStatus: string,
  accounts: Record<string, unknown>[]
): Record<string, unknown> {
  return {
    institutionId: id,
    provider,
    name,
    category,
    totalInBaseCurrency: total,
    syncStatus,
    lastSyncTimestamp: SYNCED_AT,
    lastSuccessfulSyncTimestamp: syncStatus === 'reconnect_needed' ? FROZEN_SINCE : SYNCED_AT,
    accounts,
    cards: null,
  };
}

const SUMMARY = {
  totalNetWorth: 9300,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    {
      category: 'banking',
      totalInBaseCurrency: 2500,
      institutionCount: 3,
      institutions: [
        institution('aib-1', 'truelayer', 'AIB', 'banking', 1200, 'reconnect_needed', [
          account('acc-aib', 'AIB', '4821', 'truelayer', 1200, 'reconnect_needed'),
        ]),
        institution('rev-1', 'truelayer', 'Revolut', 'banking', 800, 'retrying', [
          account('acc-rev', 'Revolut', '7710', 'truelayer', 800, 'retrying'),
        ]),
        institution('mono-1', 'monobank', 'Monobank', 'banking', 500, 'synced', [
          account('acc-black', 'Monobank', '1111', 'monobank', 500, 'synced'),
          account('acc-eaid', 'Monobank', '4321', 'monobank', 0, 'dormant'),
        ]),
      ],
    },
    {
      category: 'brokerage',
      totalInBaseCurrency: 6800,
      institutionCount: 1,
      institutions: [
        institution(
          'ibkr-1',
          'ibkr',
          'Interactive Brokers',
          'brokerage',
          6800,
          'reconnect_needed',
          [
            {
              ...account('p-googl', 'ibkr', 'GOOG', 'ibkr', 6800, 'synced'),
              accountType: 'brokerage',
              category: 'brokerage',
              symbol: 'GOOGL',
              currency: 'GOOGL',
              currentBalance: 40,
            },
          ]
        ),
      ],
    },
  ],
};

async function mockAccountsPage(page: Page): Promise<void> {
  await mockApis(page);
  await mockAccounts(page);
  await mockGet(page, 'wealth/summary', SUMMARY);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
}

async function openAccounts(page: Page, path = '/accounts/list'): Promise<void> {
  await mockAccountsPage(page);
  await page.goto(path);
}

test.describe('connection-health labels on a phone', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  test('asks to reconnect only where the owner has to act', async ({page}) => {
    await openAccounts(page);

    const rows = page.getByTestId('attention-row');
    await expect(rows).toHaveCount(2);
    await expect(rows.nth(0)).toContainText('AIB needs reconnecting');
    await expect(rows.nth(0).getByRole('button', {name: 'Reconnect'})).toBeVisible();
    // IBKR has no reconnect flow in the app: the ask stands, with no button that would start a new sign-in.
    await expect(rows.nth(1)).toContainText('Interactive Brokers needs reconnecting');
    await expect(rows.nth(1).getByRole('button', {name: 'Reconnect'})).toHaveCount(0);
  });

  test('says Retrying quietly and labels a Dormant account in its row', async ({page}) => {
    await openAccounts(page);

    await expect(page.getByText('1 account · Retrying · just now')).toBeVisible();
    await expect(page.getByTestId('account-dormant-label')).toHaveText('· Dormant');

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth
    );
    expect(overflow).toBeLessThanOrEqual(0);
  });
});

test.describe('reconnect deep link', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  test("opens the provider's existing reconnect flow and drops the parameter", async ({page}) => {
    await openAccounts(page, '/accounts/list?reconnect=truelayer');

    await expect(page.getByRole('dialog').last()).toContainText('Reconnect bank');
    await expect(page).toHaveURL(/\/accounts\/list$/);
  });

  test('lands on the row that asks for the reconnect when the provider has no flow', async ({
    page,
  }) => {
    await openAccounts(page, '/accounts/list?reconnect=ibkr');

    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByTestId('attention-row').nth(1)).toContainText(
      'Interactive Brokers needs reconnecting'
    );
  });
});

test.describe('connection-health alerts', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  const alertItem = (overrides: Record<string, unknown>) => ({
    id: 'a',
    type: 'SyncFailure',
    severity: 'Error',
    title: 'Title',
    message: '',
    referenceId: null,
    referenceLabel: null,
    isRead: false,
    isResolved: false,
    createdAt: '2026-10-16T09:00:00Z',
    resolvedAt: null,
    occurrenceCount: 1,
    lastOccurredAt: '2026-10-16T09:00:00Z',
    ...overrides,
  });

  async function openAlerts(page: Page): Promise<void> {
    await mockAccountsPage(page);
    const items = [
      alertItem({
        id: 'reconnect-1',
        title: 'Sync failed for AIB',
        message:
          "We couldn't sync your truelayer account. Please reconnect or check your credentials.",
        appPath: '/accounts/list?reconnect=truelayer',
      }),
      alertItem({
        id: 'dormant-1',
        type: 'ConnectionDormant',
        severity: 'Info',
        title: 'Monobank ···4321 may be closed',
        message:
          'It has been failing for 7 days while your other accounts sync fine.',
        appPath: '/accounts/list',
      }),
    ];
    await page.route(`${API}/alerts/*/read`, route => route.fulfill({status: 204}));
    await page.route(`${API}/alerts?**`, route =>
      route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({
          items,
          totalCount: 2,
          unreadCount: 2,
          page: 1,
          pageSize: 20,
          totalPages: 1,
        }),
      })
    );
    await page.goto('/alerts');
  }

  test('shows the one Dormant notice, which says the account may be closed', async ({page}) => {
    await openAlerts(page);

    await expect(page.getByText('Monobank ···4321 may be closed')).toBeVisible();
  });

  test('opens the reconnect from the alert of a confirmed credential loss', async ({page}) => {
    await openAlerts(page);

    await page.getByRole('button', {name: 'Sync failed for AIB'}).click();

    await expect(page.getByRole('dialog').last()).toContainText('Reconnect bank');
  });
});
