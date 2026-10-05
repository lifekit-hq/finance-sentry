import {expect, test} from '@playwright/test';

// Origin-agnostic glob — see dashboard-drilldown.spec.ts for why the dev apiBaseUrl is not used.
const API = '**/api/v1';

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: ['connections.manage'],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

// Synthetic AIB-shaped rows. AIB stamps a transaction at Irish local midnight; in October (IST,
// UTC+1) that instant arrives as 23:00 UTC on the previous evening.
const DUBLIN_MIDNIGHT_OCT_2 = '2026-10-01T23:00:00Z';
const TIMESTAMPS = '2026-10-05T00:00:00Z';

function tx(
  id: string,
  description: string,
  date: string,
  postedDate: string | null,
  isPending: boolean
): Record<string, unknown> {
  return {
    transactionId: id,
    accountId: 'acc-aib',
    bankName: 'AIB',
    currency: 'EUR',
    amount: 100,
    amountUsd: 110,
    date,
    postedDate,
    description,
    transactionType: 'debit',
    merchantCategory: null,
    isPending,
    createdAt: TIMESTAMPS,
    updatedAt: TIMESTAMPS,
  };
}

const LEDGER = {
  items: [
    tx('booked', 'Synthetic booked card row', DUBLIN_MIDNIGHT_OCT_2, DUBLIN_MIDNIGHT_OCT_2, false),
    tx('pending', 'Synthetic pending transfer row', DUBLIN_MIDNIGHT_OCT_2, null, true),
    tx('bare-date', 'Synthetic bare date row', '2026-09-15', null, false),
    tx('unparseable', 'Synthetic unparseable row', '2026-13-45', null, false),
  ],
  totalCount: 4,
  hasMore: false,
};

test.use({timezoneId: 'Europe/Dublin'});

test.describe('Transaction ledger — day grouping in the viewer timezone', () => {
  test.beforeEach(async ({page}) => {
    const json = (body: unknown) => ({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/accounts/transactions**`, route => route.fulfill(json(LEDGER)));
  });

  test('Dublin-midnight rows share the Oct 2 group, not Oct 1', async ({page}) => {
    await page.goto('/transactions');

    // "Fri, Oct 2" (2026-10-02 is a Friday); the year is appended once the date is outside "now".
    const oct2 = page.getByRole('region', {name: /^Fri, Oct 2\b/});
    await expect(oct2.getByRole('heading', {level: 2})).toBeVisible();
    await expect(oct2.getByTestId('ledger-row')).toHaveCount(2);
    await expect(oct2).toContainText('Synthetic booked card row');
    await expect(oct2).toContainText('Synthetic pending transfer row');

    await expect(page.getByRole('region', {name: /Oct 1\b/})).toHaveCount(0);
  });

  test('a bare date and an unparseable timestamp keep their own day', async ({page}) => {
    await page.goto('/transactions');

    const bare = page.getByRole('region', {name: /^Tue, Sep 15\b/});
    await expect(bare.getByTestId('ledger-row')).toHaveCount(1);
    await expect(bare).toContainText('Synthetic bare date row');

    const unparseable = page.getByRole('region', {name: '2026-13-45'});
    await expect(unparseable.getByTestId('ledger-row')).toHaveCount(1);
    await expect(unparseable).toContainText('Synthetic unparseable row');
  });
});
