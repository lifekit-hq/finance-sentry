import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const DESKTOP = {width: 1440, height: 900};
const PHONE = {width: 390, height: 844};
const LONG_COUNT = 40;
const PAGE_SIZE = 50;
const TIMESTAMP = '2026-10-05T00:00:00Z';
const SETTLE_MS = 600;

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});

async function mockApis(page: Page, count: number): Promise<void> {
  const rows = (make: (i: number) => unknown) => Array.from({length: count}, (_, i) => make(i));
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({
        items: rows(i => ({
          id: `alert-${i}`,
          type: 'Opportunity',
          severity: 'Info',
          title: `Alert ${i}`,
          message: 'A message long enough to take a row.',
          referenceId: null,
          referenceLabel: null,
          isRead: true,
          isResolved: false,
          createdAt: TIMESTAMP,
          resolvedAt: null,
        })),
        totalCount: count,
        unreadCount: 0,
        page: 1,
        pageSize: PAGE_SIZE,
        totalPages: 1,
      })
    )
  );
  await page.route(`${API}/events/upcoming**`, route => {
    const today = new Date().toISOString().slice(0, 10);
    return route.fulfill(
      json({
        items: rows(i => ({
          kind: 'macro',
          date: today,
          time: null,
          subject: 'US',
          title: `Synthetic event ${i}`,
          detail: null,
          isEstimate: false,
          source: 'https://www.federalreserve.gov',
          referenceId: null,
        })),
        from: today,
        to: today,
        sources: [],
      })
    );
  });
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill(
      json({
        items: rows(i => ({
          transactionId: `tx-${i}`,
          accountId: 'acc',
          bankName: 'Bank',
          currency: 'USD',
          amount: -10,
          amountUsd: -10,
          date: TIMESTAMP,
          postedDate: null,
          description: `Merchant ${i}`,
          transactionType: 'debit',
          merchantCategory: null,
          isPending: false,
          createdAt: TIMESTAMP,
          updatedAt: TIMESTAMP,
        })),
        totalCount: count,
        hasMore: false,
      })
    )
  );
}

// Every element that scrolls vertically right now, `main` and the document included.
function scrollers(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const found: string[] = [];
    for (const el of document.querySelectorAll('*')) {
      const overflowY = getComputedStyle(el).overflowY;
      if (/(auto|scroll)/.test(overflowY) && el.scrollHeight > el.clientHeight + 1) {
        found.push(el.tagName.toLowerCase());
      }
    }
    const doc = document.documentElement;
    if (doc.scrollHeight > doc.clientHeight + 1) {
      found.push('document');
    }
    return found;
  });
}

const PAGES = ['/alerts', '/events', '/transactions'];
const VIEWPORTS = [
  {name: '1440', size: DESKTOP},
  {name: '390', size: PHONE},
];

for (const {name, size} of VIEWPORTS) {
  test.describe(`list pages scroll only the list (${name} wide)`, () => {
    test.use({viewport: size});

    for (const path of PAGES) {
      test(`${path}: a long list is the one scroll container`, async ({page}) => {
        await mockApis(page, LONG_COUNT);
        await page.goto(path);
        await expect(page.locator('cmn-page-scroll')).toBeVisible();
        await page.waitForTimeout(SETTLE_MS);
        expect(await scrollers(page)).toEqual(['cmn-page-scroll']);
      });

      test(`${path}: an empty list scrolls nothing`, async ({page}) => {
        await mockApis(page, 0);
        await page.goto(path);
        await expect(page.locator('cmn-empty-state')).toBeVisible();
        await page.waitForTimeout(SETTLE_MS);
        expect(await scrollers(page)).toEqual([]);
      });
    }
  });
}
