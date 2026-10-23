import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const PHONE_WIDTHS = [375, 390];
const PHONE_HEIGHT = 844;
const ROWS = 30;
const SETTLE_MS = 600;
const PHONE_DEVICE_SCALE = 3;
const EDGE_STRIP_PX = 2;
const COLOR_TOLERANCE = 2;
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

// Samples the painted top edge of the page scroller: every pixel must be the sticky heading's own
// background or the page background, never a row scrolling under the heading.
async function topEdgePaintsBackground(page: Page): Promise<boolean> {
  const box = await page.locator('cmn-page-scroll').boundingBox();
  if (!box) {
    return false;
  }
  const strip = await page.screenshot({
    clip: {x: box.x, y: box.y, width: box.width, height: EDGE_STRIP_PX},
  });
  return page.evaluate(
    async ({png, tolerance}) => {
      const toRgba = (css: string): number[] => {
        const ctx = new OffscreenCanvas(1, 1).getContext('2d', {willReadFrequently: true})!;
        ctx.fillStyle = css;
        ctx.fillRect(0, 0, 1, 1);
        return [...ctx.getImageData(0, 0, 1, 1).data];
      };
      const heading = document.querySelector('cmn-page-scroll cmn-list-section h2');
      if (!heading) {
        return false;
      }
      const allowed = [
        toRgba(getComputedStyle(heading).backgroundColor),
        toRgba(getComputedStyle(document.documentElement).getPropertyValue('--color-surface-bg')),
      ];
      const blob = await (await fetch(`data:image/png;base64,${png}`)).blob();
      const bitmap = await createImageBitmap(blob);
      const ctx = new OffscreenCanvas(bitmap.width, bitmap.height).getContext('2d', {
        willReadFrequently: true,
      })!;
      ctx.drawImage(bitmap, 0, 0);
      const {data} = ctx.getImageData(0, 0, bitmap.width, bitmap.height);
      for (let i = 0; i < data.length; i += 4) {
        const matches = allowed.some(rgba =>
          [0, 1, 2].every(c => Math.abs(data[i + c] - rgba[c]) <= tolerance)
        );
        if (!matches) {
          return false;
        }
      }
      return true;
    },
    {png: strip.toString('base64'), tolerance: COLOR_TOLERANCE}
  );
}

for (const width of PHONE_WIDTHS) {
  test.describe(`mobile list overflow (${width} wide)`, () => {
    test.use({
      viewport: {width, height: PHONE_HEIGHT},
      deviceScaleFactor: PHONE_DEVICE_SCALE,
      hasTouch: true,
      isMobile: true,
    });

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
        expect(await topEdgePaintsBackground(page)).toBe(true);
      });
    }
  });
}
