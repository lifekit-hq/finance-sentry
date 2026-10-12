import {expect, type Locator, type Page, test} from '@playwright/test';

import {API, AUTH_RESPONSE} from './support/dossier-mocks';

// The hero card must keep one height across every range and across loading: the delta and the
// "At this pace" lines own fixed slots, so a range where a line does not apply (or whose history is
// still in flight) never moves the content below the figure.
const RANGES = ['1W', 'MTD', '1M', '3M', 'YTD', '1Y', 'ALL'];
const HISTORY_LATENCY_MS = 400;
const SAMPLE_WINDOW_MS = HISTORY_LATENCY_MS + 500;
const DESKTOP = {width: 1280, height: 900};
const PHONE = {width: 390, height: 900};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

const snapshot = (snapshotDate: string, banking: number) => ({
  snapshotDate,
  bankingTotal: banking,
  brokerageTotal: 5000,
  cryptoTotal: 1000,
  totalNetWorth: banking + 6000,
  currency: 'USD',
});

const TWO_SNAPSHOTS = [snapshot('2026-07-01', 40_000), snapshot('2026-08-01', 43_000)];
// One snapshot yields no delta, so the delta line does not apply for that response.
const ONE_SNAPSHOT = [snapshot('2026-08-01', 43_000)];

const DASHBOARD = {
  aggregatedBalance: {USD: 50_000},
  totalNetWorthUsd: 50_000,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [],
  topCategories: [],
  lastSyncTimestamp: null,
};

async function mockApis(page: Page): Promise<void> {
  let historyCalls = 0;
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.route(`${API}/net-worth/history**`, async route => {
    historyCalls += 1;
    // First load shows the delta; the calls after alternate, so lines come and go while cycling.
    const snapshots = historyCalls % 2 === 1 ? TWO_SNAPSHOTS : ONE_SNAPSHOT;
    await new Promise(resolve => setTimeout(resolve, HISTORY_LATENCY_MS));
    await route.fulfill(json({snapshots, hasHistory: true}));
  });
}

function heroCard(page: Page): Locator {
  return page.locator('cmn-card', {has: page.locator('lk-segmented')}).first();
}

/** Every distinct rendered height of the card seen on each frame during `windowMs`. */
async function sampleHeights(card: Locator, windowMs: number): Promise<number[]> {
  return card.evaluate(
    (el, ms) =>
      new Promise<number[]>(resolve => {
        const seen = new Set<number>();
        const end = performance.now() + ms;
        const tick = (): void => {
          seen.add(el.getBoundingClientRect().height);
          if (performance.now() < end) {
            requestAnimationFrame(tick);
          } else {
            resolve([...seen]);
          }
        };
        tick();
      }),
    windowMs
  );
}

async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth
  );
  expect(overflow).toBeLessThanOrEqual(0);
}

for (const [name, viewport] of [
  ['desktop', DESKTOP],
  ['390px phone', PHONE],
] as const) {
  test.describe(`Dashboard hero height, ${name}`, () => {
    test('stays constant across every range, loading and loaded', async ({page}) => {
      await page.setViewportSize(viewport);
      await mockApis(page);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-value')).toContainText('$50,000.00');
      await expect(page.getByTestId('net-worth-change')).toBeVisible();
      const card = heroCard(page);
      const baseline = (await card.boundingBox())?.height ?? 0;
      expect(baseline).toBeGreaterThan(0);
      // The tiles reload with the same range; their loading skeleton must match the loaded value.
      const tiles = page.locator('cmn-card', {has: page.locator('button[aria-label]')}).first();
      const tilesBaseline = (await tiles.boundingBox())?.height ?? 0;

      for (const range of RANGES) {
        const heights = sampleHeights(card, SAMPLE_WINDOW_MS);
        const tileHeights = sampleHeights(tiles, SAMPLE_WINDOW_MS);
        await page.getByRole('radio', {name: range, exact: true}).click();
        expect(await heights, `range ${range}`).toEqual([baseline]);
        expect(await tileHeights, `tiles, range ${range}`).toEqual([tilesBaseline]);
      }
      await expectNoHorizontalScroll(page);
    });

    test('stays constant while scrubbing the chart', async ({page}) => {
      await page.setViewportSize(viewport);
      await mockApis(page);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-change')).toBeVisible();
      const card = heroCard(page);
      const baseline = (await card.boundingBox())?.height ?? 0;
      const canvas = card.locator('canvas').first();
      await expect(canvas).toBeVisible();
      const box = await canvas.boundingBox();
      if (!box) {
        throw new Error('chart canvas has no box');
      }

      const heights = sampleHeights(card, SAMPLE_WINDOW_MS);
      // A mouse scrubs on hover; pressing and releasing on the canvas is a click, which opens the
      // band's accounts page and leaves the dashboard.
      await page.mouse.move(box.x + box.width * 0.3, box.y + box.height / 2);
      await page.mouse.move(box.x + box.width * 0.7, box.y + box.height / 2, {steps: 10});
      expect(await heights).toEqual([baseline]);
      await expectNoHorizontalScroll(page);
    });
  });
}
