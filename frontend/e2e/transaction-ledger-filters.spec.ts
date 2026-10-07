import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const DEBOUNCE_SETTLE_MS = 600;
const PHONE_VIEWPORT = {width: 390, height: 844};
const FILTER_TEST_IDS = [
  'account-filter',
  'category-filter',
  'date-filter',
  'min-amount-filter',
  'max-amount-filter',
  'clear-filters',
];

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: []},
  expiresAt: '2027-01-01T00:00:00Z',
};

const ACCOUNTS = {
  accounts: [
    {accountId: 'acc-aib', bankName: 'AIB', accountNumberLast4: '1111'},
    {accountId: 'acc-mono', bankName: 'Monobank', accountNumberLast4: '2222'},
  ],
};
const CATEGORIES = [
  {key: 'FOOD_AND_DRINK', label: 'Food & Drink'},
  {key: 'TRANSPORT', label: 'Transport'},
];

function tx(id: string, description: string): Record<string, unknown> {
  return {
    transactionId: id,
    accountId: 'acc-aib',
    bankName: 'AIB',
    currency: 'EUR',
    amount: 10,
    amountUsd: 11,
    date: '2026-10-01T10:00:00Z',
    postedDate: '2026-10-01T10:00:00Z',
    description,
    transactionType: 'debit',
    merchantCategory: null,
    isPending: false,
    createdAt: '2026-10-05T00:00:00Z',
    updatedAt: '2026-10-05T00:00:00Z',
  };
}

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

/** Records every `accounts/transactions` query string; the server "matches" unless search is `nomatch`. */
async function mockApi(page: Page): Promise<URLSearchParams[]> {
  const requests: URLSearchParams[] = [];
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/categories`, route => route.fulfill(json(CATEGORIES)));
  await page.route(`${API}/accounts`, route => route.fulfill(json(ACCOUNTS)));
  await page.route(`${API}/accounts/transactions**`, route => {
    const query = new URL(route.request().url()).searchParams;
    requests.push(query);
    const items = query.get('search') === 'nomatch' ? [] : [tx('t1', 'Synthetic coffee row')];
    return route.fulfill(json({items, totalCount: items.length, hasMore: false}));
  });
  return requests;
}

const last = (requests: URLSearchParams[]) => requests[requests.length - 1];

test.describe('Transaction ledger — server-side filters with URL sync', () => {
  test('deep link hydrates every filter into the API query and Clear filters resets both', async ({
    page,
  }) => {
    const requests = await mockApi(page);
    await page.goto(
      '/transactions?account=acc-aib&category=FOOD_AND_DRINK&type=credit&from=2026-09-01&to=2026-09-30&minAmountUsd=10&maxAmountUsd=500&search=coffee'
    );
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);

    const q = last(requests);
    expect(q.getAll('accountId')).toEqual(['acc-aib']);
    expect(q.getAll('category')).toEqual(['FOOD_AND_DRINK']);
    expect(q.get('transactionType')).toBe('credit');
    expect(q.get('from')).toBe('2026-09-01');
    expect(q.get('to')).toBe('2026-09-30');
    expect(q.get('minAmountUsd')).toBe('10');
    expect(q.get('maxAmountUsd')).toBe('500');
    expect(q.get('search')).toBe('coffee');
    await expect(page.getByRole('searchbox', {name: 'Search transactions'})).toHaveValue('coffee');
    await expect(page.getByTestId('type-in')).toBeVisible();
    await page.screenshot({path: 'playwright-report/ledger-filters-deeplink.png'});

    await page.getByTestId('clear-filters').click();
    await expect(page.getByTestId('clear-filters')).toHaveCount(0);
    await expect.poll(() => page.url()).not.toMatch(/minAmountUsd|search=|type=|from=|account=/);
    const cleared = last(requests);
    expect([...cleared.keys()].sort()).toEqual(['limit', 'offset']);
  });

  test('a stale or hand-edited link is ignored instead of hitting the API with garbage', async ({
    page,
  }) => {
    const requests = await mockApi(page);
    await page.goto('/transactions?type=foo&from=garbage&to=2026-13&minAmountUsd=abc');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);

    const q = last(requests);
    for (const key of ['transactionType', 'from', 'to', 'minAmountUsd', 'maxAmountUsd']) {
      expect(q.has(key), key).toBe(false);
    }
    await expect(page.getByRole('alert')).toHaveCount(0);
    await expect(page.getByTestId('clear-filters')).toHaveCount(0);
    await expect(page.getByTestId('min-amount-filter').locator('input')).toHaveValue('');
  });

  test('type chips, search and amount bounds drive the query and the URL', async ({page}) => {
    const requests = await mockApi(page);
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    expect(requests).toHaveLength(1);
    expect([...last(requests).keys()].sort()).toEqual(['limit', 'offset']);

    await page.getByTestId('type-out').click();
    await expect.poll(() => last(requests).get('transactionType')).toBe('debit');
    expect(page.url()).toContain('type=debit');

    const search = page.getByRole('searchbox', {name: 'Search transactions'});
    await search.fill('nomatch');
    await expect.poll(() => last(requests).get('search')).toBe('nomatch');
    await expect(page.getByTestId('ledger-empty')).toContainText('No matching transactions');
    expect(page.url()).toContain('search=nomatch');

    await search.fill('');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    expect(last(requests).has('search')).toBe(false);

    const min = page.getByTestId('min-amount-filter').locator('input');
    await min.fill('25');
    await expect.poll(() => last(requests).get('minAmountUsd')).toBe('25');
    expect(page.url()).toContain('minAmountUsd=25');
    await page.screenshot({path: 'playwright-report/ledger-filters-active.png'});
  });

  test('typing 1.05 with pauses is never rewritten to 1 mid-edit', async ({page}) => {
    const requests = await mockApi(page);
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    const min = page.getByTestId('min-amount-filter').locator('input');

    await min.pressSequentially('1', {delay: 0});
    await page.waitForTimeout(DEBOUNCE_SETTLE_MS);
    await min.pressSequentially('.', {delay: 0});
    await page.waitForTimeout(DEBOUNCE_SETTLE_MS);
    await min.pressSequentially('0', {delay: 0});
    await page.waitForTimeout(DEBOUNCE_SETTLE_MS);
    await expect(min).toHaveValue('1.0');
    await min.pressSequentially('5', {delay: 0});
    await expect.poll(() => last(requests).get('minAmountUsd')).toBe('1.05');
    await expect(min).toHaveValue('1.05');
  });

  test('a pending bound survives another field committing (min then max within the debounce)', async ({
    page,
  }) => {
    const requests = await mockApi(page);
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    const min = page.getByTestId('min-amount-filter').locator('input');
    const max = page.getByTestId('max-amount-filter').locator('input');

    await min.fill('5');
    await page.waitForTimeout(150);
    await max.fill('100');
    await expect.poll(() => last(requests).get('maxAmountUsd')).toBe('100');
    await expect.poll(() => last(requests).get('minAmountUsd')).toBe('5');
    await expect(max).toHaveValue('100');
    await expect(min).toHaveValue('5');

    // Same for search committing while an amount is still pending.
    const search = page.getByRole('searchbox', {name: 'Search transactions'});
    await min.fill('7');
    await page.waitForTimeout(150);
    await search.fill('coffee');
    await expect.poll(() => last(requests).get('search')).toBe('coffee');
    await expect.poll(() => last(requests).get('minAmountUsd')).toBe('7');
    await expect(min).toHaveValue('7');
  });

  test('Clear filters within the debounce cancels the pending amount', async ({page}) => {
    const requests = await mockApi(page);
    await page.goto('/transactions?type=debit');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    const min = page.getByTestId('min-amount-filter').locator('input');

    await min.fill('50');
    await page.getByTestId('clear-filters').click();
    await page.waitForTimeout(DEBOUNCE_SETTLE_MS * 2);

    expect(requests.some(r => r.get('minAmountUsd') === '50')).toBe(false);
    expect(page.url()).not.toContain('minAmountUsd');
    await expect(min).toHaveValue('');
    await expect(page.getByTestId('clear-filters')).toHaveCount(0);
  });

  test('account and category multi-selects apply to the query and URL', async ({page}) => {
    const requests = await mockApi(page);
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);

    await page.getByTestId('account-filter').getByRole('button').first().click();
    await page.getByRole('option', {name: /Monobank/}).click();
    await expect.poll(() => last(requests).getAll('accountId')).toEqual(['acc-mono']);
    expect(page.url()).toContain('account=acc-mono');
    await page.keyboard.press('Escape');

    await page.getByTestId('category-filter').getByRole('button').first().click();
    await page.getByRole('option', {name: /Transport/}).click();
    await expect.poll(() => last(requests).getAll('category')).toEqual(['TRANSPORT']);
    expect(page.url()).toContain('category=TRANSPORT');
  });

  test('date range inputs apply from/to to the query and URL', async ({page}) => {
    const requests = await mockApi(page);
    await page.goto('/transactions');
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);

    const dates = page.getByTestId('date-filter').locator('input[type="date"]');
    await dates.nth(0).fill('2026-09-01');
    await expect.poll(() => last(requests).get('from')).toBe('2026-09-01');
    await dates.nth(1).fill('2026-09-30');
    await expect.poll(() => last(requests).get('to')).toBe('2026-09-30');
    expect(page.url()).toContain('from=2026-09-01');
    expect(page.url()).toContain('to=2026-09-30');
  });

  test('the filter bar fits a 390px phone with every filter active', async ({page}) => {
    await page.setViewportSize(PHONE_VIEWPORT);
    await mockApi(page);
    await page.goto(
      '/transactions?account=acc-aib&category=FOOD_AND_DRINK&type=credit&from=2026-09-01&to=2026-09-30&minAmountUsd=10&maxAmountUsd=500&search=coffee'
    );
    await expect(page.getByTestId('ledger-row')).toHaveCount(1);
    await expect(page.getByTestId('clear-filters')).toBeVisible();

    const overflow = await page.evaluate(() => ({
      scroll: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
    }));
    expect(overflow.scroll).toBeLessThanOrEqual(overflow.client);

    const withinViewport = (box: {x: number; width: number} | null) => {
      expect(box).not.toBeNull();
      expect(box?.x).toBeGreaterThanOrEqual(0);
      expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(PHONE_VIEWPORT.width);
    };
    withinViewport(await page.getByRole('searchbox', {name: 'Search transactions'}).boundingBox());
    for (const testId of FILTER_TEST_IDS) {
      const control = page.getByTestId(testId);
      await expect(control, testId).toBeVisible();
      withinViewport(await control.boundingBox());
    }

    await page.getByTestId('category-filter').getByRole('button').first().click();
    const panel = page.locator('.cdk-overlay-pane').first();
    await expect(panel).toBeVisible();
    withinViewport(await panel.boundingBox());
  });
});
