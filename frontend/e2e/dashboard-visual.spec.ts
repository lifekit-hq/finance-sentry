import {expect, type Page, test} from '@playwright/test';

import {API, AUTH_RESPONSE} from './support/dossier-mocks';

// Pixel baseline for the dashboard, same pattern as the asset dossier spec: route-mocked fixtures,
// a clock that starts at a pinned time so month keys and relative timestamps are stable, CSS
// animations disabled by the screenshot options. Light theme (the default), desktop and 390 px phone.
// The clock is installed (ticking) rather than frozen: Chart.js animates off Date.now, so a frozen
// clock leaves the bar chart at zero height. Capture waits for the chart canvas, then advances the
// fake clock past the animation (CHART_SETTLE_MS) so the bars are drawn at their full data height.
const FROZEN_NOW = new Date('2026-09-15T12:30:00Z');
const CHART_SETTLE_MS = 3000;
// The app shell scrolls inside its own container, so a full-page capture only sees the viewport:
// the viewports are tall enough to hold the whole dashboard.
const DESKTOP = {width: 1280, height: 1200};
const PHONE = {width: 390, height: 1500};
const SCREENSHOT_OPTIONS = {
  animations: 'disabled',
  // Glyph anti-aliasing differs a little between the machines that render these baselines.
  maxDiffPixelRatio: 0.001,
} as const;

const DASHBOARD = {
  aggregatedBalance: {USD: 50000},
  totalNetWorthUsd: 50000,
  baseCurrency: 'USD',
  accountCount: 3,
  accountsByType: {banking: 2, brokerage: 1},
  monthlyFlow: [
    {
      month: '2026-08',
      currency: 'USD',
      inflow: 5000,
      outflow: 3000,
      net: 2000,
      inflowUsd: 5000,
      outflowUsd: 3000,
      netUsd: 2000,
      familySupportOutflowUsd: 0,
    },
    {
      month: '2026-09',
      currency: 'USD',
      inflow: 4800,
      outflow: 2900,
      net: 1900,
      inflowUsd: 4800,
      outflowUsd: 2900,
      netUsd: 1900,
      familySupportOutflowUsd: 800,
      investedOutflowUsd: 600,
    },
  ],
  topCategories: [
    {category: 'FOOD_AND_DRINK', totalSpend: 800, percentOfTotal: 30},
    {category: 'TRAVEL', totalSpend: 500, percentOfTotal: 18},
  ],
  lastSyncTimestamp: '2026-09-15T10:30:00Z',
};

const json = (body: unknown) => ({
  status: 200,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

// Enough snapshots for the hero chart, so the baseline shows the figure, chart and range control as one card.
const HISTORY = [
  ['2026-07-15', 41_000, 5_000, 1_000],
  ['2026-08-15', 42_500, 5_500, 1_200],
  ['2026-09-01', 43_000, 5_800, 1_100],
  ['2026-09-15', 43_000, 5_800, 1_200],
].map(([snapshotDate, bankingTotal, brokerageTotal, cryptoTotal]) => ({
  snapshotDate,
  bankingTotal,
  brokerageTotal,
  cryptoTotal,
  totalNetWorth: Number(bankingTotal) + Number(brokerageTotal) + Number(cryptoTotal),
  currency: 'USD',
}));

async function openDashboard(page: Page): Promise<void> {
  await page.clock.install({time: FROZEN_NOW});
  await page.route(`${API}/auth/me`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/auth/refresh`, route => route.fulfill(json(AUTH_RESPONSE)));
  await page.route(`${API}/dashboard/aggregated**`, route => route.fulfill(json(DASHBOARD)));
  await page.route(`${API}/net-worth/history**`, route =>
    route.fulfill(json({snapshots: HISTORY, hasHistory: true}))
  );
  await page.route(`${API}/categories`, route => route.fulfill(json([])));
  await page.route(`${API}/accounts`, route => route.fulfill(json({accounts: []})));
  await page.goto('/dashboard');
  await expect(page.getByTestId('net-worth-value')).toContainText('$50,000.00');
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('canvas').first()).toBeVisible();
  await page.clock.runFor(CHART_SETTLE_MS);
}

async function expectBaseline(page: Page, name: string): Promise<void> {
  // The sidebar footer prints the app version, which changes on every release.
  const version = page.getByText(/^v\d+\.\d+\.\d+$/);
  await expect(page).toHaveScreenshot(name, {...SCREENSHOT_OPTIONS, mask: [version]});
}

test.describe('Dashboard visual baseline', () => {
  test('dashboard on desktop', async ({page}) => {
    await page.setViewportSize(DESKTOP);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-desktop.png');
  });

  test('dashboard on a phone', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-phone.png');
  });
});
