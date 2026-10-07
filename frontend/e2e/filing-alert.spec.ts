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

const FILING_URL =
  'https://www.sec.gov/Archives/edgar/data/320193/000032019324000123/aapl-20240630.htm';

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
      id: 'filing-1',
      type: 'FilingLanded',
      title: 'AAPL filed a 10-Q',
      message: `AAPL filed a 10-Q on 2024-08-02. ${FILING_URL}`,
      isRead: false,
    }),
    alertItem({
      id: 'low-1',
      type: 'LowBalance',
      title: 'Low balance on Revolut',
      message: 'Balance dropped to 12.3456 EUR',
    }),
  ],
  totalCount: 2,
  unreadCount: 1,
  page: 1,
  pageSize: 20,
  totalPages: 1,
};

async function mockApis(page: Page): Promise<void> {
  const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 1})));
  await page.route(`${API}/alerts/*/read`, route => route.fulfill({status: 204}));
  await page.route(`${API}/alerts?**`, route => route.fulfill(json(ALERTS)));
  await page.route(`${API}/accounts**`, route => route.fulfill(json([])));
  // The filing opens on sec.gov; the sandbox must not reach the real site.
  await page
    .context()
    .route('https://www.sec.gov/**', route =>
      route.fulfill({contentType: 'text/html', body: '<h1>stub EDGAR filing</h1>'})
    );
}

test.describe('Filing alerts', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('shows the sentence without the raw SEC URL', async ({page}) => {
    await page.goto('/alerts');

    const row = page.getByRole('button').filter({hasText: 'AAPL filed a 10-Q'});
    await expect(row).toContainText('AAPL filed a 10-Q on 2024-08-02.');
    await expect(row).not.toContainText('sec.gov');
    await expect(row).not.toContainText('https://');
  });

  test('clicking a filing alert opens the filing in a new tab and stays on /alerts', async ({
    page,
  }) => {
    await page.goto('/alerts');

    const popupPromise = page.waitForEvent('popup');
    await page.getByRole('button').filter({hasText: 'AAPL filed a 10-Q'}).click();
    const popup = await popupPromise;

    expect(popup.url()).toBe(FILING_URL);
    expect(await popup.evaluate(() => window.opener)).toBeNull();
    await expect(page).toHaveURL(/\/alerts$/);
  });

  test('a non-filing alert still routes to /accounts/list and opens no tab', async ({page}) => {
    await page.goto('/alerts');
    let popups = 0;
    page.on('popup', () => popups++);

    await page.getByRole('button').filter({hasText: 'Low balance on Revolut'}).click();

    await expect(page).toHaveURL(/\/accounts\/list$/);
    expect(popups).toBe(0);
  });
});
