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

const TIMESTAMPS = '2026-10-05T00:00:00Z';

function tx(id: string, description: string, date: string): Record<string, unknown> {
  return {
    transactionId: id,
    accountId: 'acc-1',
    bankName: 'AIB',
    currency: 'EUR',
    amount: 10,
    amountUsd: 11,
    date,
    postedDate: date,
    description,
    transactionType: 'debit',
    merchantCategory: null,
    isPending: false,
    createdAt: TIMESTAMPS,
    updatedAt: TIMESTAMPS,
  };
}

test.describe('Transaction ledger — quick period chips', () => {
  test('a chip sets the date filter and narrows the list; picking a custom range clears it', async ({
    page,
  }) => {
    const json = (body: unknown) => ({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    // Stand-in for the backend: honour from/to the way the real endpoint does.
    await page.route(`${API}/accounts/transactions**`, route => {
      const url = new URL(route.request().url());
      const from = url.searchParams.get('from');
      const to = url.searchParams.get('to');
      const all = [
        tx('old', 'Synthetic old row', '2020-01-15'),
        tx('now', 'Synthetic current row', new Date().toISOString().slice(0, 10)),
      ];
      const items = all.filter(
        t => (!from || String(t.date) >= from) && (!to || String(t.date) <= to)
      );
      return route.fulfill(json({items, totalCount: items.length, hasMore: false}));
    });

    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(2);

    await page.getByTestId('period-this-month').click();
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await expect(page.getByText('Synthetic current row')).toBeVisible();
    await expect(page.getByTestId('period-this-month').getByRole('button')).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    await expect(page.getByTestId('chip-date-range')).toHaveAttribute(
      'label',
      'Period: This month'
    );
    await expect(page).toHaveURL(/from=\d{4}-\d{2}-01&to=\d{4}-\d{2}-\d{2}/);

    // A custom range matches no period chip; the Dates chip names the range instead.
    await page.goto('/transactions?from=2020-01-01&to=2020-01-31');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await expect(page.getByTestId('period-this-month').getByRole('button')).toHaveAttribute(
      'aria-pressed',
      'false'
    );

    // Clearing the dates clears the chip and restores the full list.
    await page.getByTestId('chip-date-range').getByRole('button').click();
    await expect(page.getByTestId('ledger-row')).toHaveCount(2);
  });
});
