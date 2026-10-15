import {expect, type Page, test} from '@playwright/test';

import {API, AUTH_RESPONSE} from './support/dossier-mocks';

// A change across a window that holds a linked or removed account is mostly the book changing size,
// so the hero states the amount without a percentage and names why, on the one change line.
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

const SNAPSHOTS = [snapshot('2026-07-01', 40_000), snapshot('2026-08-01', 43_000)];

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

async function mockApis(page: Page, scopeChanges: unknown): Promise<void> {
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(
      json({
        snapshots: SNAPSHOTS,
        hasHistory: true,
        ...(scopeChanges === undefined ? {} : {scopeChanges}),
      })
    )
  );
}

for (const [name, viewport] of [
  ['desktop', DESKTOP],
  ['390px phone', PHONE],
] as const) {
  test.describe(`Net-worth change across a scope change, ${name}`, () => {
    test.beforeEach(async ({page}) => {
      await page.setViewportSize(viewport);
    });

    test('a linked account hides the percentage and names why', async ({page}) => {
      await mockApis(page, [{source: 'monobank', kind: 'linked', at: '2026-07-15'}]);
      await page.goto('/dashboard');
      const change = page.getByTestId('net-worth-change');
      await expect(change).toContainText('+$3,000');
      await expect(change).not.toContainText('%');
      const note = page.getByTestId('net-worth-scope-note');
      await expect(note).toContainText('Includes newly linked accounts');
      await expect(note).toHaveAttribute('title', 'Includes newly linked accounts');
      await expect(note).toHaveAttribute('aria-label', 'Includes newly linked accounts');
      await page.screenshot({
        path: `/home/denys/.no-mistakes/evidence/01M4YBYP97SCSGW4Q4VKGGDTED/scope-note-linked-${viewport.width}.png`,
      });
    });

    test('a removed account names removal', async ({page}) => {
      await mockApis(page, [{source: 'monobank', kind: 'removed', at: '2026-07-20'}]);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-scope-note')).toContainText(
        'Includes removed accounts'
      );
      await expect(page.getByTestId('net-worth-change')).not.toContainText('%');
    });

    test('no scope change keeps the percentage and shows no note', async ({page}) => {
      await mockApis(page, []);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-change')).toContainText('%');
      await expect(page.getByTestId('net-worth-scope-note')).toHaveCount(0);
      await page.screenshot({
        path: `/home/denys/.no-mistakes/evidence/01M4YBYP97SCSGW4Q4VKGGDTED/scope-note-none-${viewport.width}.png`,
      });
    });

    test('an older API without scopeChanges keeps the percentage', async ({page}) => {
      await mockApis(page, undefined);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-change')).toContainText('%');
      await expect(page.getByTestId('net-worth-scope-note')).toHaveCount(0);
    });

    test('a scope change outside the window does not hide the percentage', async ({page}) => {
      await mockApis(page, [{source: 'monobank', kind: 'linked', at: '2026-06-01'}]);
      await page.goto('/dashboard');
      await expect(page.getByTestId('net-worth-change')).toContainText('%');
      await expect(page.getByTestId('net-worth-scope-note')).toHaveCount(0);
    });

    test('the note does not wrap the change line or add a horizontal scroll', async ({page}) => {
      await mockApis(page, [{source: 'monobank', kind: 'linked', at: '2026-07-15'}]);
      await page.goto('/dashboard');
      const change = page.getByTestId('net-worth-change');
      await expect(page.getByTestId('net-worth-scope-note')).toBeVisible();
      const box = await change.boundingBox();
      expect(box?.height).toBeLessThanOrEqual(20);
      const overflow = await page.evaluate(
        () => document.documentElement.scrollWidth - document.documentElement.clientWidth
      );
      expect(overflow).toBeLessThanOrEqual(0);
    });
  });
}
