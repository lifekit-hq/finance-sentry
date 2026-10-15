import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE = {width: 390, height: 844};
const INSET_TOP = 47;
const INSET_BOTTOM = 34;
const ROW_COUNT = 30;
const ALERT_PAGE_SIZE = 20;
const PIXEL_TOLERANCE = 1;

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};
const TIMESTAMP = '2026-10-05T00:00:00Z';

const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});

async function mockApis(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/alerts?**`, route => {
    const items = Array.from({length: ALERT_PAGE_SIZE}, (_, i) => ({
      id: `alert-${i}`,
      type: 'Opportunity',
      severity: 'Info',
      title: `Alert ${i}`,
      message: 'Long enough to overflow the phone viewport.',
      referenceId: null,
      referenceLabel: null,
      isRead: false,
      isResolved: false,
      createdAt: '2026-10-01T09:00:00Z',
      resolvedAt: null,
    }));
    return route.fulfill(
      json({
        items,
        totalCount: ROW_COUNT,
        unreadCount: 0,
        page: 1,
        pageSize: ALERT_PAGE_SIZE,
        totalPages: 2,
      })
    );
  });
  await page.route(`${API}/events/upcoming**`, route => {
    const today = new Date().toISOString().slice(0, 10);
    const items = Array.from({length: ROW_COUNT}, (_, i) => ({
      kind: 'macro',
      date: today,
      time: null,
      subject: 'US',
      title: `Synthetic event ${i}`,
      detail: null,
      isEstimate: false,
      source: 'https://www.federalreserve.gov',
      referenceId: null,
    }));
    return route.fulfill(json({items, from: today, to: today, sources: []}));
  });
  await page.route(`${API}/dashboard/flow-breakdown**`, route => {
    const items = Array.from({length: ROW_COUNT}, (_, i) => ({
      transactionId: `flow-${i}`,
      accountId: 'acc-1',
      bankName: 'AIB',
      accountLast4: '4821',
      currency: 'USD',
      amount: -10,
      amountUsd: -10,
      date: '2026-09-14T06:00:00Z',
      description: `Synthetic spend ${i}`,
      merchantName: `Synthetic spend ${i}`,
      category: null,
      direction: 'out',
      bucket: 'spending',
      counterpartyName: null,
      flowRole: null,
    }));
    return route.fulfill(json({month: '2026-09', items}));
  });
  await page.route(`${API}/agent/conversations`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts/transactions**`, route => {
    const items = Array.from({length: ROW_COUNT}, (_, i) => ({
      transactionId: `tx-${i}`,
      accountId: 'acc-1',
      bankName: 'AIB',
      currency: 'EUR',
      amount: 10,
      amountUsd: 11,
      date: '2026-10-05',
      postedDate: '2026-10-05',
      description: `Synthetic row ${i}`,
      transactionType: 'debit',
      merchantCategory: null,
      isPending: false,
      createdAt: TIMESTAMP,
      updatedAt: TIMESTAMP,
    }));
    return route.fulfill(json({items, totalCount: ROW_COUNT * 2, hasMore: true}));
  });
}

// Installed-PWA shape: the safe-area insets are non-zero. Chromium simulates them through CDP.
async function simulateInsets(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setSafeAreaInsetsOverride', {
    insets: {top: INSET_TOP, bottom: INSET_BOTTOM, left: 0, right: 0},
  });
}

// After scrolling the content region to its end, the last piece of content ends at or above the
// tab bar's top edge, so nothing at the bottom of a page sits under the floating bar.
async function expectLastContentClearsTabBar(page: Page, selector: string): Promise<void> {
  await page.locator('cmn-app-layout main').evaluate(el => {
    el.scrollTop = el.scrollHeight;
  });
  const last = page.locator(selector).last();
  await last.scrollIntoViewIfNeeded();
  const tabBar = await page.locator('cmn-bottom-tab-bar nav').boundingBox();
  const content = await last.boundingBox();
  if (!tabBar || !content) {
    throw new Error('tab bar or last content has no box');
  }
  expect(content.y + content.height).toBeLessThanOrEqual(tabBar.y + PIXEL_TOLERANCE);
}

test.describe('last content clears the tab bar (390 wide, iPhone insets)', () => {
  test.use({viewport: PHONE, hasTouch: true, isMobile: true});

  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('Transactions: Load More sits above the tab bar', async ({page}) => {
    await page.goto('/transactions');
    await simulateInsets(page);
    await page.reload();
    await expect(page.getByTestId('ledger-row')).toHaveCount(ROW_COUNT);

    await expectLastContentClearsTabBar(
      page,
      'fns-transaction-ledger cmn-button:has-text("Load More")'
    );
  });

  test('Alerts: the pager sits above the tab bar', async ({page}) => {
    await page.goto('/alerts');
    await simulateInsets(page);
    await page.reload();
    await expect(page.getByTestId('alert-row')).toHaveCount(ALERT_PAGE_SIZE);

    await expectLastContentClearsTabBar(page, '[data-testid="alerts-pager"]');
  });

  test('Events: the last day of the calendar sits above the tab bar', async ({page}) => {
    await page.goto('/events');
    await simulateInsets(page);
    await page.reload();
    await expect(page.getByText(`Synthetic event ${ROW_COUNT - 1}`)).toBeVisible();

    await expectLastContentClearsTabBar(page, 'fns-events section[data-testid^="day-"]');
  });

  test('Flow breakdown: the last row sits above the tab bar', async ({page}) => {
    await page.goto('/dashboard/breakdown?month=2026-09');
    await simulateInsets(page);
    await page.reload();
    const rows = page.locator('[data-testid="breakdown-rows"] [data-testid="breakdown-row"]');
    await expect(rows).toHaveCount(ROW_COUNT);

    await expectLastContentClearsTabBar(
      page,
      '[data-testid="breakdown-rows"] [data-testid="breakdown-row"]'
    );
  });

  test('Ledger chat: the composer sits above the tab bar', async ({page}) => {
    await page.goto('/ledger');
    await simulateInsets(page);
    await page.reload();
    await expect(page.getByRole('textbox', {name: 'Ask Ledger…'})).toBeVisible();

    await expectLastContentClearsTabBar(page, 'role=textbox[name="Ask Ledger…"]');
  });
});
