import {expect, type Page, type Request, test} from '@playwright/test';

const API = '**/api/v1';
const PAGE_SIZE = 20;
const TOTAL = 45;

const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: ['connections.manage', 'ai.use', 'ops.admin'],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

function alertItem(overrides: Record<string, unknown>) {
  return {
    id: 'a',
    type: 'LowBalance',
    severity: 'Info',
    title: 'Title',
    message: '',
    referenceId: null,
    referenceLabel: null,
    isRead: true,
    isResolved: false,
    createdAt: '2026-10-01T09:00:00Z',
    resolvedAt: null,
    occurrenceCount: 1,
    lastOccurredAt: '2026-10-01T09:00:00Z',
    ...overrides,
  };
}

function pageOf(pageNumber: number, pageSize: number, items: unknown[]) {
  return {
    items,
    totalCount: TOTAL,
    unreadCount: 3,
    page: pageNumber,
    pageSize,
    totalPages: Math.ceil(TOTAL / pageSize),
  };
}

const FIRST_PAGE_ITEMS = [
  alertItem({
    id: 'open-1',
    type: 'DuplicateCharge',
    title: 'Possible duplicate charge',
    isRead: false,
    occurrenceCount: 7,
  }),
  alertItem({
    id: 'resolved-1',
    type: 'LowBalance',
    title: 'Low balance on Revolut',
    isResolved: true,
    resolvedAt: '2026-10-02T09:00:00Z',
  }),
  alertItem({
    id: 'asset-1',
    type: 'EarningsAhead',
    title: 'AAPL reports earnings soon',
    referenceLabel: 'AAPL',
  }),
  alertItem({id: 'budget-1', type: 'BudgetBreach', title: 'Groceries budget exceeded'}),
  alertItem({id: 'job-1', type: 'JobFailure', title: 'A background job failed'}),
];

const SECOND_PAGE_ITEMS = [
  alertItem({id: 'p2-1', type: 'PriceHike', title: 'Streaming price hike'}),
];

async function mockApis(page: Page): Promise<Request[]> {
  const listRequests: Request[] = [];
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 3})));
  await page.route(`${API}/alerts/*/read`, route => route.fulfill({status: 204}));
  await page.route(`${API}/alerts?**`, route => {
    const request = route.request();
    listRequests.push(request);
    const url = new URL(request.url());
    const pageNumber = Number(url.searchParams.get('page'));
    const pageSize = Number(url.searchParams.get('pageSize'));
    const items = pageNumber === 1 ? FIRST_PAGE_ITEMS : SECOND_PAGE_ITEMS;
    return route.fulfill(json(pageOf(pageNumber, pageSize, items)));
  });
  await page.route(`${API}/accounts**`, route => route.fulfill(json([])));
  await page.route(`${API}/budgets**`, route => route.fulfill(json([])));
  await page.route(`${API}/transactions**`, route =>
    route.fulfill(json({items: [], totalCount: 0}))
  );
  return listRequests;
}

test.describe('Alerts page', () => {
  let listRequests: Request[];

  test.beforeEach(async ({page}) => {
    listRequests = await mockApis(page);
  });

  test('pages through the server-side list', async ({page}) => {
    await page.goto('/alerts');
    await expect(page.getByTestId('alerts-page-label')).toHaveText('Page 1 of 3');
    await expect(page.getByTestId('alerts-prev').getByRole('button')).toBeDisabled();
    expect(new URL(listRequests[0].url()).searchParams.get('pageSize')).toBe(String(PAGE_SIZE));

    await page.getByTestId('alerts-next').click();

    await expect(page.getByTestId('alerts-page-label')).toHaveText('Page 2 of 3');
    await expect(page.getByText('Streaming price hike')).toBeVisible();
    expect(new URL(listRequests[listRequests.length - 1].url()).searchParams.get('page')).toBe('2');

    await page.getByTestId('alerts-prev').click();
    await expect(page.getByTestId('alerts-page-label')).toHaveText('Page 1 of 3');
    await expect(page.getByText('Possible duplicate charge')).toBeVisible();
  });

  test('changing the page size reloads from the first page', async ({page}) => {
    await page.goto('/alerts');
    await page.getByTestId('alerts-next').click();
    await expect(page.getByTestId('alerts-page-label')).toHaveText('Page 2 of 3');

    await page.getByRole('button', {name: '50 per page'}).click();

    await expect(page.getByTestId('alerts-page-label')).toHaveText('Page 1 of 1');
    const last = new URL(listRequests[listRequests.length - 1].url());
    expect(last.searchParams.get('page')).toBe('1');
    expect(last.searchParams.get('pageSize')).toBe('50');
  });

  test('shows resolved alerts muted and the repeat count as ×N', async ({page}) => {
    await page.goto('/alerts');

    const resolved = page.locator('[data-resolved="true"]');
    await expect(resolved).toHaveCount(1);
    await expect(resolved).toContainText('Low balance on Revolut');
    await expect(resolved).toContainText('Resolved');
    await expect(resolved).toHaveClass(/opacity-60/);

    const repeated = page.getByTestId('alert-row').filter({hasText: 'Possible duplicate charge'});
    await expect(repeated).toContainText('×7');
    await expect(repeated).not.toHaveClass(/opacity-60/);
  });

  test('routes each alert type to its own page', async ({page}) => {
    await page.goto('/alerts');
    await page.getByRole('button', {name: 'Possible duplicate charge'}).click();
    await expect(page).toHaveURL(/\/transactions$/);

    await page.goto('/alerts');
    await page.getByRole('button', {name: 'Groceries budget exceeded'}).click();
    await expect(page).toHaveURL(/\/budgets$/);

    await page.goto('/alerts');
    await page.getByRole('button', {name: 'AAPL reports earnings soon'}).click();
    await expect(page).toHaveURL(/\/assets\/AAPL$/);
  });

  test('an alert with no destination stays on /alerts', async ({page}) => {
    await page.goto('/alerts');
    await page.getByRole('button', {name: 'A background job failed'}).click();
    await expect(page).toHaveURL(/\/alerts$/);
  });
});
