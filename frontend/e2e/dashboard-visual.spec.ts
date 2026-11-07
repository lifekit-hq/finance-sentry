import {expect, type Page, test} from '@playwright/test';

import {mockDashboard} from './support/dashboard-mocks';
import {versionMasks} from './support/visual-baseline';

// Pixel baseline for the dashboard, same pattern as the asset dossier spec: route-mocked fixtures,
// a clock that starts at a pinned time so month keys and relative timestamps are stable, CSS
// animations disabled by the screenshot options. Light theme (the default) and dark, desktop and 390 px phone.
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

async function openDashboard(page: Page): Promise<void> {
  await page.clock.install({time: FROZEN_NOW});
  await mockDashboard(page);
  await page.goto('/dashboard');
  await expect(page.getByTestId('net-worth-value')).toContainText('$50,000.00');
  await expect(page.getByTestId('net-worth-allocation')).toBeVisible();
  await expect(page.getByTestId('net-worth-why-moved-change')).toBeVisible();
  await expect(page.getByTestId('top-alert-more')).toHaveText('2 more in Alerts ›');
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('canvas').first()).toBeVisible();
  await page.clock.runFor(CHART_SETTLE_MS);
}

async function expectBaseline(page: Page, name: string): Promise<void> {
  await expect(page).toHaveScreenshot(name, {...SCREENSHOT_OPTIONS, mask: versionMasks(page)});
}

test.describe('Dashboard visual baseline', () => {
  test('dashboard on desktop', async ({page}) => {
    await page.setViewportSize(DESKTOP);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-desktop.png');
  });

  test('dashboard on desktop, dark', async ({page}) => {
    await page.emulateMedia({colorScheme: 'dark'});
    await page.setViewportSize(DESKTOP);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-desktop-dark.png');
  });

  test('dashboard on a phone, dark', async ({page}) => {
    await page.emulateMedia({colorScheme: 'dark'});
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-phone-dark.png');
  });

  test('dashboard on a phone', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openDashboard(page);
    await expectBaseline(page, 'dashboard-phone.png');
  });
});
