import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE_WIDTHS = [375, 390];
const PHONE_HEIGHT = 844;
const ROWS = 30;
const SETTLE_MS = 600;
const TIMESTAMP = '2026-10-05T00:00:00Z';

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com', roles: ['Owner'], permissions: ['ai.use']},
  expiresAt: '2027-01-01T00:00:00Z',
};

const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});

// Dates spread over several days so the list renders several sticky day headers.
const daysAgo = (i: number) =>
  new Date(Date.now() - Math.floor(i / 3) * 86_400_000 - i * 60_000).toISOString();

async function mockApis(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: ROWS})));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/alerts?**`, route =>
    route.fulfill(
      json({
        items: Array.from({length: ROWS}, (_, i) => ({
          id: `alert-${i}`,
          type: i % 2 ? 'MarketMove' : 'NewsCluster',
          severity: 'Warning',
          title: i % 2 ? 'Unusual move: USDT-USD' : 'News cluster: DRAM',
          message: 'DRAM news clustered on 2026-10-23: thesis-attached source hit',
          referenceId: null,
          referenceLabel: i % 2 ? 'USDT-USD' : 'DRAM',
          isRead: false,
          isResolved: false,
          createdAt: daysAgo(i),
          resolvedAt: null,
          occurrenceCount: 13,
          lastOccurredAt: daysAgo(i),
        })),
        totalCount: ROWS,
        unreadCount: ROWS,
        page: 1,
        pageSize: 50,
        totalPages: 1,
      })
    )
  );
  await page.route(`${API}/accounts/transactions**`, route =>
    route.fulfill(
      json({
        items: Array.from({length: ROWS}, (_, i) => ({
          transactionId: `tx-${i}`,
          accountId: 'acc',
          bankName: 'Bank',
          currency: 'USD',
          amount: -10,
          amountUsd: -10,
          date: daysAgo(i),
          postedDate: null,
          description: `A rather long merchant description number ${i}`,
          transactionType: 'debit',
          merchantCategory: null,
          isPending: false,
          createdAt: TIMESTAMP,
          updatedAt: TIMESTAMP,
        })),
        totalCount: ROWS,
        hasMore: false,
      })
    )
  );
  await page.route(`${API}/accounts`, route => route.fulfill(json([])));
}

// Elements whose right edge sits past the viewport, plus any scroll container that scrolls sideways.
function overflowing(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const width = document.documentElement.clientWidth;
    const found: string[] = [];
    for (const el of document.querySelectorAll('main *, cmn-page-scroll, cmn-page-scroll *')) {
      const rect = el.getBoundingClientRect();
      if (rect.width > 0 && rect.right > width + 1) {
        found.push(`${el.tagName.toLowerCase()}.${String(el.className).slice(0, 60)}`);
      }
    }
    for (const el of document.querySelectorAll('*')) {
      if (
        /(auto|scroll)/.test(getComputedStyle(el).overflowX) &&
        el.scrollWidth > el.clientWidth + 1
      ) {
        found.push(`scrollX:${el.tagName.toLowerCase()}`);
      }
    }
    return found;
  });
}

// What paints on the scroller's first pixel rows: the sticky day heading, not a row scrolling under it.
function topEdgeIsHeading(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const scroller = document.querySelector('cmn-page-scroll');
    if (!scroller) {
      return false;
    }
    const {top, left, width} = scroller.getBoundingClientRect();
    return [0.5, 1.5, 3].every(offset =>
      document.elementFromPoint(left + width / 2, top + offset)?.closest('cmn-list-section h2')
    );
  });
}

for (const width of PHONE_WIDTHS) {
  test.describe(`mobile list overflow (${width} wide)`, () => {
    test.use({viewport: {width, height: PHONE_HEIGHT}});

    for (const {path, rows} of [
      {path: '/alerts', rows: 'alert-row'},
      {path: '/transactions', rows: 'ledger-row'},
    ]) {
      test(`${path}: no horizontal overflow and no gap above the sticky day header`, async ({
        page,
      }) => {
        await mockApis(page);
        await page.goto(path);
        await expect(page.getByTestId(rows).first()).toBeVisible();
        await page.waitForTimeout(SETTLE_MS);

        await page.locator('cmn-page-scroll').evaluate(el => {
          const scroller = [el, ...el.querySelectorAll('*')].find(
            e => getComputedStyle(e).overflowY === 'auto' && e.scrollHeight > e.clientHeight
          );
          scroller?.scrollBy(0, 400);
        });
        await page.waitForTimeout(SETTLE_MS);

        expect(await overflowing(page)).toEqual([]);
        expect(await topEdgeIsHeading(page)).toBe(true);
      });
    }
  });
}
