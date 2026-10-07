import {expect, type Page, test} from '@playwright/test';

import {API, LEDGER_READ_NARRATIVE, mockApis} from './support/dossier-mocks';

// Pixel baseline for the asset dossier: guards the page against unintended visual drift while its
// markup moves between hand-rolled blocks and shared-library components. Scope is the dossier only
// (not the wider app-surface rail). The clock is pinned so relative timestamps ("Updated 2h ago")
// are stable, and animations are disabled by the screenshot options below.
const FROZEN_NOW = new Date('2026-09-01T12:30:00Z');
// The app shell scrolls inside its own container, so a full-page capture only sees the viewport:
// the viewports are tall enough to hold the whole dossier.
const DESKTOP = {width: 1280, height: 2800};
const PHONE = {width: 375, height: 2400};
// The library stat cards are bordered tiles, so the full dossier outgrows PHONE on a phone.
const PHONE_FULL = {width: 375, height: 3000};
const SCREENSHOT_OPTIONS = {
  animations: 'disabled',
  // Glyph anti-aliasing differs a little between the machines that render these baselines.
  maxDiffPixelRatio: 0.02,
} as const;

async function openDossier(
  page: Page,
  symbol: string,
  overrideRoutes?: () => Promise<void>
): Promise<void> {
  await page.clock.setFixedTime(FROZEN_NOW);
  await mockApis(page);
  await overrideRoutes?.();
  await page.goto(`/assets/${symbol}`);
  await expect(page.getByRole('heading', {name: symbol, level: 1})).toBeVisible();
  await page.evaluate(() => document.fonts.ready);
}

// The compact trend chart does not animate, so once its canvas has drawn pixels the capture shows
// the real line rather than a frozen intermediate frame.
async function expectTrendDrawn(page: Page): Promise<void> {
  const canvas = page.locator('cmn-line-chart canvas');
  await expect(canvas).toBeVisible();
  await expect
    .poll(() =>
      canvas.evaluate(el => {
        const c = el as HTMLCanvasElement;
        const data = c.getContext('2d')?.getImageData(0, 0, c.width, c.height).data;
        return data ? data.some((v, i) => i % 4 === 3 && v > 0) : false;
      })
    )
    .toBe(true);
}

test.describe('Asset dossier visual baseline', () => {
  test('full dossier on desktop', async ({page}) => {
    await page.setViewportSize(DESKTOP);
    await openDossier(page, 'AAPL');
    await expect(page.getByTestId('ledger-read-empty')).toBeVisible();
    await expectTrendDrawn(page);
    await expect(page).toHaveScreenshot('dossier-desktop.png', SCREENSHOT_OPTIONS);
  });

  test('full dossier on a phone', async ({page}) => {
    await page.setViewportSize(PHONE_FULL);
    await openDossier(page, 'AAPL');
    await expect(page.getByTestId('trend-list')).toBeVisible();
    await expectTrendDrawn(page);
    await expect(page).toHaveScreenshot('dossier-phone.png', SCREENSHOT_OPTIONS);
  });

  test("dossier with a generated Ledger's read", async ({page}) => {
    await page.setViewportSize(DESKTOP);
    await openDossier(page, 'AAPL', () =>
      page.route(`${API}/research/assets/AAPL/narrative**`, route =>
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            symbol: 'AAPL',
            narrative: LEDGER_READ_NARRATIVE,
            generatedAt: '2026-09-01T10:00:00Z',
            isStale: true,
            cached: true,
          }),
        })
      )
    );
    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expectTrendDrawn(page);
    await expect(page).toHaveScreenshot('dossier-ledger-read.png', SCREENSHOT_OPTIONS);
  });

  test('dossier with nothing on file', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openDossier(page, 'ZZZZ');
    await expect(page.getByTestId('dossier-no-data')).toBeVisible();
    await expect(page).toHaveScreenshot('dossier-no-data.png', SCREENSHOT_OPTIONS);
  });
});
