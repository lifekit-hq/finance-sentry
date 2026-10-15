import {expect, type Page, test} from '@playwright/test';

import {mockAccounts} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {mockGet} from './support/visual-baseline';

const PHONES = {
  'iPhone SE 375x812': {width: 375, height: 812},
  'iPhone 390x844': {width: 390, height: 844},
  'iPhone Plus 402x874': {width: 402, height: 874},
} as const;
const THREE_DAYS_MS = 3 * 24 * 60 * 60 * 1000;
const FROZEN_SINCE = new Date(Date.now() - THREE_DAYS_MS).toISOString();
const SYNCED_AT = new Date().toISOString();
const MAX_SCREENS = 2;

function position(
  id: string,
  symbol: string,
  quantity: number,
  value: number,
  category: 'brokerage' | 'crypto',
  provider: string
): Record<string, unknown> {
  return {
    accountId: id,
    bankName: provider,
    accountType: category,
    accountNumberLast4: symbol.slice(0, 4),
    currency: symbol,
    symbol,
    provider,
    category,
    currentBalance: quantity,
    balanceInBaseCurrency: value,
    syncStatus: 'synced',
    lastSyncTimestamp: SYNCED_AT,
  };
}

function institution(
  id: string,
  provider: string,
  name: string,
  category: 'banking' | 'brokerage' | 'crypto',
  total: number,
  accounts: Record<string, unknown>[],
  syncStatus = 'synced'
): Record<string, unknown> {
  return {
    institutionId: id,
    provider,
    name,
    category,
    totalInBaseCurrency: total,
    syncStatus,
    lastSyncTimestamp: SYNCED_AT,
    lastSuccessfulSyncTimestamp: syncStatus === 'synced' ? SYNCED_AT : FROZEN_SINCE,
    accounts,
    cards: null,
  };
}

const AIB_BALANCE = 2400;
const GOOGL_VALUE = 6800;
const BTC_VALUE = 3200;

const SUMMARY = {
  totalNetWorth: AIB_BALANCE + GOOGL_VALUE + BTC_VALUE,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    {
      category: 'banking',
      totalInBaseCurrency: AIB_BALANCE,
      institutionCount: 1,
      institutions: [
        institution(
          'aib-1',
          'truelayer',
          'AIB',
          'banking',
          AIB_BALANCE,
          [
            {
              accountId: 'acc-aib',
              bankName: 'AIB',
              accountType: 'current',
              accountNumberLast4: '4821',
              currency: 'USD',
              provider: 'truelayer',
              category: 'banking',
              currentBalance: AIB_BALANCE,
              balanceInBaseCurrency: AIB_BALANCE,
              syncStatus: 'reauth_required',
              lastSyncTimestamp: FROZEN_SINCE,
            },
          ],
          'reauth_required'
        ),
      ],
    },
    {
      category: 'brokerage',
      totalInBaseCurrency: GOOGL_VALUE,
      institutionCount: 1,
      institutions: [
        institution('ibkr-1', 'ibkr', 'Interactive Brokers', 'brokerage', GOOGL_VALUE, [
          position('p-googl', 'GOOGL', 40, GOOGL_VALUE, 'brokerage', 'ibkr'),
        ]),
      ],
    },
    {
      category: 'crypto',
      totalInBaseCurrency: BTC_VALUE,
      institutionCount: 1,
      institutions: [
        institution('binance-1', 'binance', 'Binance', 'crypto', BTC_VALUE, [
          position('p-btc', 'BTC', 0.0812, BTC_VALUE, 'crypto', 'binance'),
        ]),
      ],
    },
  ],
};

const MANY_IBKR = 14;
const MANY_BINANCE = 9;

// The shape that made the page long: a broker and an exchange with a full portfolio each.
function heavySummary(): typeof SUMMARY {
  const positions = (count: number, prefix: string, category: 'brokerage' | 'crypto', p: string) =>
    Array.from({length: count}, (_, i) =>
      position(`${prefix}-${i}`, `${prefix}${i}`.toUpperCase(), 1, 100, category, p)
    );
  const [banking, brokerage, crypto] = SUMMARY.categories;
  return {
    ...SUMMARY,
    categories: [
      banking,
      {
        ...brokerage,
        institutions: [
          institution(
            'ibkr-1',
            'ibkr',
            'Interactive Brokers',
            'brokerage',
            MANY_IBKR * 100,
            positions(MANY_IBKR, 'sym', 'brokerage', 'ibkr')
          ),
        ],
      },
      {
        ...crypto,
        institutions: [
          institution(
            'binance-1',
            'binance',
            'Binance',
            'crypto',
            MANY_BINANCE * 100,
            positions(MANY_BINANCE, 'coin', 'crypto', 'binance')
          ),
        ],
      },
    ],
  };
}

async function openAccounts(page: Page, summary: unknown = SUMMARY): Promise<void> {
  await mockApis(page);
  await mockAccounts(page);
  await mockGet(page, 'wealth/summary', summary);
  await page.route(`${API}/alerts/unread-count`, route =>
    route.fulfill({contentType: 'application/json', body: JSON.stringify({count: 0})})
  );
  await page.goto('/accounts/list');
}

for (const [name, viewport] of Object.entries(PHONES)) {
  test.describe(`accounts on a phone (${name})`, () => {
    test.use({viewport, hasTouch: true, isMobile: true});

    test('puts the reconnect row first, with a visible Reconnect button', async ({page}) => {
      await openAccounts(page);

      const row = page.getByTestId('attention-row');
      await expect(row).toBeVisible();
      await expect(row).toContainText('AIB needs reconnecting');
      await expect(row).toContainText('Balances frozen since 3d ago');
      await expect(row.getByRole('button', {name: 'Reconnect'})).toBeVisible();
    });

    test('shows subtotals and one nav row per brokerage and exchange, with no sideways scroll', async ({
      page,
    }) => {
      await openAccounts(page);

      await expect(page.getByTestId('section-subtotal').first()).toBeVisible();
      await expect(page.getByTestId('position-ticker')).toHaveCount(0);
      const rows = page.getByTestId('institution-nav-row');
      await expect(rows).toHaveCount(2);
      await expect(rows.first()).toContainText('Interactive Brokers');
      await expect(rows.first().getByTestId('institution-nav-sublabel')).toHaveText('1 position');
      await expect(rows.first().getByTestId('institution-nav-total')).toHaveText('$6,800.00');
      await expect(rows.nth(1).getByTestId('institution-nav-sublabel')).toHaveText('1 asset');

      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth
      );
      expect(overflow).toBeLessThanOrEqual(0);
    });

    test('opens Investments filtered to the provider and keeps the menu reachable', async ({
      page,
    }) => {
      await openAccounts(page);

      const row = page.getByTestId('institution-nav-row').first();
      await expect(
        row.getByRole('button', {name: 'Actions for Interactive Brokers'})
      ).toBeVisible();
      await row.getByTestId('institution-nav-link').click();

      await expect(page).toHaveURL(/\/accounts\/investments\?provider=ibkr/);
    });

    test('keeps a 23-position portfolio within two screens', async ({page}) => {
      await openAccounts(page, heavySummary());
      await expect(page.getByTestId('institution-nav-row').first()).toBeVisible();

      const screens = await page.evaluate(
        () => document.documentElement.scrollHeight / window.innerHeight
      );
      expect(screens).toBeLessThanOrEqual(MAX_SCREENS);
    });
  });
}
