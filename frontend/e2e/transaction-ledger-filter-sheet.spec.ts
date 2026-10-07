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

function tx(
  id: string,
  description: string,
  date: string,
  type: 'debit' | 'credit',
  category: string | null
): Record<string, unknown> {
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
    transactionType: type,
    merchantCategory: category,
    isPending: false,
    createdAt: TIMESTAMPS,
    updatedAt: TIMESTAMPS,
  };
}

const CATEGORIES = [
  {key: 'FOOD_AND_DRINK', label: 'Food & Drink', sortOrder: 10},
  {key: 'TRANSPORT', label: 'Transport', sortOrder: 20},
];

test.describe('Transaction ledger — filter sheet', () => {
  test('Period + Type + Category apply from the sheet, show as chips, dismiss and reset', async ({
    page,
  }) => {
    const json = (body: unknown) => ({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });
    const today = new Date().toISOString().slice(0, 10);
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/categories`, route => route.fulfill(json(CATEGORIES)));
    // Stand-in for the backend: honour the filters the way the real endpoint does.
    await page.route(`${API}/accounts/transactions**`, route => {
      const q = new URL(route.request().url()).searchParams;
      const all = [
        tx('old', 'Synthetic old row', '2020-01-15', 'debit', 'FOOD_AND_DRINK'),
        tx('food', 'Synthetic food row', today, 'debit', 'FOOD_AND_DRINK'),
        tx('ride', 'Synthetic ride row', today, 'debit', 'TRANSPORT'),
        tx('pay', 'Synthetic pay row', today, 'credit', null),
      ];
      const categories = q.getAll('category');
      const items = all.filter(
        t =>
          (!q.get('from') || String(t.date) >= q.get('from')!) &&
          (!q.get('to') || String(t.date) <= q.get('to')!) &&
          (!q.get('transactionType') || t.transactionType === q.get('transactionType')) &&
          (categories.length === 0 || categories.includes(String(t.merchantCategory)))
      );
      return route.fulfill(json({items, totalCount: items.length, hasMore: false}));
    });

    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(4);
    await expect(page.getByTestId('applied-filters')).toHaveCount(0);

    await page.getByTestId('filter-button').getByRole('button').click();
    await page.getByTestId('period-1y').getByRole('button').click();
    await page.getByTestId('type-out').getByRole('button').click();
    await page.getByTestId('category-FOOD_AND_DRINK').getByRole('button').click();
    await page.getByTestId('filter-apply').getByRole('button').click();

    // Only today's food debit survives; the filters round-trip through the URL.
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await expect(page.getByText('Synthetic food row')).toBeVisible();
    await expect(page).toHaveURL(/type=debit/);
    await expect(page).toHaveURL(/category=FOOD_AND_DRINK/);
    await expect(page).toHaveURL(/from=\d{4}-\d{2}-\d{2}&/);
    await expect(page.getByTestId('chip-date-range')).toHaveAttribute('label', 'Period: 1Y');
    await expect(page.getByTestId('chip-type')).toHaveAttribute('label', 'Type: Out');
    await expect(page.getByTestId('chip-category-FOOD_AND_DRINK')).toHaveAttribute(
      'label',
      'Category: Food & Drink'
    );

    // Dismissing a chip widens the list; Clear removes the rest.
    await page.getByTestId('chip-category-FOOD_AND_DRINK').getByRole('button').click();
    await expect(page.getByTestId('ledger-row')).toHaveCount(2);
    await page.getByTestId('filter-clear').getByRole('button').click();
    await expect(page.getByTestId('ledger-row')).toHaveCount(4);
    await expect(page.getByTestId('applied-filters')).toHaveCount(0);

    // Reset in the sheet empties the draft before it is applied.
    await page.goto('/transactions?type=credit');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await page.getByTestId('filter-button').getByRole('button').click();
    await page.getByTestId('filter-reset').getByRole('button').click();
    await page.getByTestId('filter-apply').getByRole('button').click();
    await expect(page.getByTestId('ledger-row')).toHaveCount(4);
    await expect(page).not.toHaveURL(/type=/);
  });

  test('a deep link with a custom range shows a Dates chip and opens the sheet on Custom', async ({
    page,
  }) => {
    const json = (body: unknown) => ({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/categories`, route => route.fulfill(json(CATEGORIES)));
    await page.route(`${API}/accounts/transactions**`, route =>
      route.fulfill(json({items: [], totalCount: 0, hasMore: false}))
    );

    await page.goto('/transactions?from=2020-01-01&to=2020-01-31');
    await expect(page.getByTestId('chip-date-range')).toHaveAttribute(
      'label',
      'Dates: 2020-01-01 – 2020-01-31'
    );
    await page.getByTestId('filter-button').getByRole('button').click();
    await expect(page.getByTestId('period-custom').getByRole('button')).toHaveAttribute(
      'aria-pressed',
      'true'
    );
  });

  test('at 390px: search + filter button, the account chip row, then rows, with no sideways scroll', async ({
    page,
  }) => {
    const json = (body: unknown) => ({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(body),
    });
    await page.setViewportSize({width: 390, height: 800});
    await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
    await page.route(`${API}/categories`, route => route.fulfill(json(CATEGORIES)));
    await page.route(`${API}/accounts`, route =>
      route.fulfill(
        json({
          accounts: ['a1', 'a2'].map(id => ({
            accountId: id,
            bankName: 'AIB',
            displayName: id,
            currency: 'EUR',
            balance: 1,
            balanceUsd: 1,
            status: 'active',
          })),
        })
      )
    );
    await page.route(`${API}/accounts/transactions**`, route =>
      route.fulfill(
        json({
          items: [tx('t1', 'Synthetic row', '2026-10-05', 'debit', null)],
          totalCount: 1,
          hasMore: false,
        })
      )
    );

    await page.goto('/transactions?type=debit');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    const y = async (locator: ReturnType<typeof page.locator>) =>
      (await locator.boundingBox())?.y ?? Number.NaN;
    const search = await y(page.getByRole('searchbox', {name: 'Search transactions'}));
    const button = await y(page.getByTestId('filter-button'));
    const accounts = await y(page.getByRole('group', {name: 'Account filter'}));
    const row = await y(page.getByTestId('ledger-row'));
    expect(Math.abs(search - button)).toBeLessThan(20);
    expect(accounts).toBeGreaterThan(search);
    expect(row).toBeGreaterThan(accounts);
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)
    ).toBe(true);
  });
});
