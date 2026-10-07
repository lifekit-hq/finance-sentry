import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';

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
    ...overrides,
  };
}

const ALERTS = {
  items: [
    alertItem({
      id: 'low-1',
      type: 'LowBalance',
      title: 'Low balance on Revolut',
      appPath: '/transactions?account=acc-1',
    }),
    alertItem({
      id: 'budget-1',
      type: 'BudgetBreach',
      title: 'Groceries over budget',
      appPath: '/transactions?category=Groceries&from=2026-10-01&to=2026-10-31',
    }),
    alertItem({
      id: 'legacy-1',
      type: 'PriceHike',
      title: 'Netflix raised its price',
    }),
    alertItem({
      id: 'hostile-1',
      type: 'PriceHike',
      title: 'Hostile path alert',
      appPath: 'https://evil.example/x',
    }),
  ],
  totalCount: 4,
  unreadCount: 0,
  page: 1,
  pageSize: 20,
  totalPages: 1,
};

async function mockApis(page: Page): Promise<void> {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/alerts/*/read`, route => route.fulfill({status: 204}));
  await page.route(`${API}/alerts?**`, route => route.fulfill(json(ALERTS)));
  await page.route(`${API}/accounts**`, route => route.fulfill(json([])));
  await page.route(`${API}/transactions**`, route =>
    route.fulfill(json({items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0}))
  );
}

test.describe('Alerts open the server-resolved appPath', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await page.goto('/alerts');
  });

  test('a LowBalance alert opens the ledger filtered to its account', async ({page}) => {
    await page.getByRole('button').filter({hasText: 'Low balance on Revolut'}).click();
    await expect(page).toHaveURL(/\/transactions\?account=acc-1$/);
  });

  test('a budget breach keeps every query parameter of its path', async ({page}) => {
    await page.getByRole('button').filter({hasText: 'Groceries over budget'}).click();
    await expect(page).toHaveURL(
      /\/transactions\?category=Groceries&from=2026-10-01&to=2026-10-31$/
    );
  });

  test('an alert without appPath falls back to its type destination', async ({page}) => {
    await page.getByRole('button').filter({hasText: 'Netflix raised its price'}).click();
    await expect(page).toHaveURL(/\/subscriptions$/);
  });

  test('an absolute-URL appPath is ignored and never leaves the app', async ({page}) => {
    let popups = 0;
    page.on('popup', () => popups++);
    await page.getByRole('button').filter({hasText: 'Hostile path alert'}).click();
    await expect(page).toHaveURL(/\/subscriptions$/);
    expect(popups).toBe(0);
  });
});
