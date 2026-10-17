import {expect, type Locator, type Page, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

// Phone widths and heights the app must hold at (iPhone SE / 8, mini, 12-14, 15-16, Pixel, Plus/Max).
const WIDTHS = [320, 360, 375, 390, 393, 412, 430];
const HEIGHTS = [667, 844, 932];
const INSET_TOP = 62;
const INSET_BOTTOM = 34;
const MIN_TARGET_PX = 44;
const MIN_FIELD_FONT_PX = 16;
const GUTTER_PX = 16;
const DESKTOP = {width: 1440, height: 900};
// Where the screenshots of the shell, a sheet, a full-screen flow and an action sheet land.
const PROOF_DIR = process.env['PHONE_PROOF_DIR'];

// iOS home-screen web app: viewport units resolve to the full screen while the page starts under
// the status bar, so anything sized by them overflows by the inset (see phone-overlay.spec.ts).
const VIEWPORT_UNITS_TALLER_BY_INSET = `
  .h-screen, .h-dvh, .h-svh, .h-lvh { height: calc(100dvh + ${INSET_TOP}px) !important; }
  .min-h-screen, .min-h-dvh, .min-h-svh, .min-h-lvh { min-height: calc(100dvh + ${INSET_TOP}px) !important; }
`;

const json = (body: unknown) => ({contentType: 'application/json', body: JSON.stringify(body)});

const BUDGETS = {
  year: 2026,
  month: 9,
  items: [
    {
      id: 'b-1',
      category: 'GROCERIES',
      categoryLabel: 'Groceries',
      monthlyLimit: 600,
      spent: 280,
      remaining: 320,
      isOverBudget: false,
      currency: 'USD',
      paceRatio: 0.47,
      projectedMonthEndSpend: 560,
      isOffPace: false,
    },
  ],
  totalLimit: 600,
  totalSpent: 280,
};

const WATCHLIST = [
  {
    id: 'w1',
    ticker: 'PLTR',
    exchange: 'NASDAQ',
    note: 'Add on a pullback',
    addedAt: '2026-10-14T09:00:00Z',
  },
];

async function open(page: Page, path: string): Promise<void> {
  await mockApis(page);
  await page.route(`${API}/alerts/unread-count`, route => route.fulfill(json({count: 0})));
  await page.route(`${API}/budgets/summary**`, route => route.fulfill(json(BUDGETS)));
  await page.route(`${API}/research/watchlist`, route => route.fulfill(json(WATCHLIST)));
  await page.goto(path);
}

async function simulateInsets(page: Page): Promise<void> {
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setSafeAreaInsetsOverride', {
    insets: {top: INSET_TOP, bottom: INSET_BOTTOM, left: 0, right: 0},
  });
}

async function box(
  locator: Locator
): Promise<{x: number; y: number; width: number; height: number}> {
  const b = await locator.boundingBox();
  if (!b) {
    throw new Error('element has no box');
  }
  return b;
}

async function proof(page: Page, name: string): Promise<void> {
  if (PROOF_DIR) {
    const {width, height} = page.viewportSize() ?? {width: 0, height: 0};
    await page.screenshot({path: `${PROOF_DIR}/${name}-${width}x${height}.png`});
  }
}

for (const width of WIDTHS) {
  for (const height of HEIGHTS) {
    test.describe(`phone shell ${width}x${height}`, () => {
      test.use({viewport: {width, height}, hasTouch: true, isMobile: true});

      test('tab bar on the bottom edge, nothing overflows, targets and gutters hold', async ({
        page,
      }) => {
        await open(page, '/budgets');
        await simulateInsets(page);
        await page.reload();
        await page.addStyleTag({content: VIEWPORT_UNITS_TALLER_BY_INSET});
        await expect(page.getByText('Groceries').first()).toBeVisible();

        const overflow = await page.evaluate(() => ({
          x: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          y: document.documentElement.scrollHeight - document.documentElement.clientHeight,
        }));
        expect(overflow.x).toBeLessThanOrEqual(0);
        expect(overflow.y).toBeLessThanOrEqual(0);

        const tabBar = await box(page.locator('cmn-bottom-tab-bar'));
        expect(tabBar.y + tabBar.height).toBeCloseTo(height, 0);
        expect(tabBar.x).toBe(0);
        expect(tabBar.width).toBe(width);

        const tabs = page.locator('cmn-bottom-tab-bar nav button');
        for (const tab of await tabs.all()) {
          expect((await box(tab)).height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
        }

        const clipped = await page.evaluate(() =>
          Array.from(document.querySelectorAll('main *'))
            .filter(el => el.children.length === 0 && el.textContent?.trim())
            .filter(el => el.getBoundingClientRect().right > document.documentElement.clientWidth)
            .map(el => el.textContent?.trim())
        );
        expect(clipped).toEqual([]);

        const heading = await box(page.getByRole('heading', {name: 'Budgets', level: 1}));
        expect(heading.x).toBeGreaterThanOrEqual(GUTTER_PX);
        await proof(page, 'shell-budgets');
      });
    });
  }
}

test.describe('phone dialogs', () => {
  for (const viewport of [
    {width: 390, height: 844},
    {width: 320, height: 667},
  ]) {
    test.describe(`${viewport.width}x${viewport.height}`, () => {
      test.use({viewport, hasTouch: true, isMobile: true});

      test('a form opens as a bottom sheet with 16px+ fields', async ({page}) => {
        await open(page, '/budgets');
        await page.getByRole('button', {name: 'Add budget'}).click();
        const dialog = page.locator('.cmn-dialog-shell');
        await expect(dialog).toBeVisible();
        await expect(page.locator('cmn-dialog-container')).toHaveAttribute(
          'data-presentation',
          'sheet'
        );
        await page.waitForTimeout(600);

        const sheet = await box(dialog);
        expect(sheet.y).toBeGreaterThan(0);
        expect(sheet.y + sheet.height).toBeCloseTo(viewport.height, 0);
        expect(sheet.width).toBe(viewport.width);

        const fonts = await dialog
          .locator('input, textarea, select')
          .evaluateAll(els => els.map(el => parseFloat(getComputedStyle(el).fontSize)));
        expect(fonts.length).toBeGreaterThan(0);
        for (const font of fonts) {
          expect(font).toBeGreaterThanOrEqual(MIN_FIELD_FONT_PX);
        }
        await proof(page, 'sheet-add-budget');
      });

      test('the connect flow is full-screen', async ({page}) => {
        await open(page, '/accounts');
        await page
          .getByRole('button', {name: /Connect/})
          .first()
          .click();
        const dialog = page.locator('.cmn-dialog-shell');
        await expect(dialog).toBeVisible();
        await expect(page.locator('cmn-dialog-container')).toHaveAttribute(
          'data-presentation',
          'full-screen'
        );
        await page.waitForTimeout(600);

        const full = await box(dialog);
        expect(full.x).toBe(0);
        expect(full.y).toBe(0);
        expect(full.width).toBe(viewport.width);
        expect(full.height).toBe(viewport.height);

        const spill = await page.evaluate(() =>
          Array.from(document.querySelectorAll('cmn-selectable-card *'))
            .filter(el => el.children.length === 0)
            .filter(
              el =>
                el.getBoundingClientRect().right >
                (el.closest('cmn-selectable-card')?.getBoundingClientRect().right ?? 0)
            )
            .map(el => el.textContent?.trim())
        );
        expect(spill).toEqual([]);
        await proof(page, 'full-screen-connect');
      });

      test('a confirm is an action sheet with a separate Cancel', async ({page}) => {
        await open(page, '/watchlist');
        await page.getByTestId('watchlist-remove').first().click();
        const dialog = page.locator('.cmn-dialog-shell');
        await expect(dialog).toBeVisible();
        await expect(page.locator('cmn-dialog-container')).toHaveAttribute(
          'data-presentation',
          'action-sheet'
        );
        await page.waitForTimeout(600);

        const cancel = page.getByRole('button', {name: 'Keep'});
        await expect(cancel).toBeVisible();
        expect((await box(cancel)).height).toBeGreaterThanOrEqual(MIN_TARGET_PX);
        expect((await box(dialog)).y).toBeGreaterThan(0);
        await proof(page, 'action-sheet-remove');

        await cancel.click();
        await expect(dialog).toBeHidden();
      });
    });
  }

  test.describe('desktop is unchanged', () => {
    test.use({viewport: DESKTOP});

    test('a form and a confirm stay centred dialogs', async ({page}) => {
      await open(page, '/budgets');
      await page.getByRole('button', {name: 'Add budget'}).click();
      const dialog = page.locator('.cmn-dialog-shell');
      await expect(dialog).toBeVisible();
      await expect(page.locator('cmn-dialog-container')).toHaveAttribute(
        'data-presentation',
        'dialog'
      );
      await page.waitForTimeout(600);

      const form = await box(dialog);
      expect(form.y).toBeGreaterThan(0);
      expect(form.y + form.height).toBeLessThan(DESKTOP.height);
      expect(form.width).toBeLessThan(DESKTOP.width / 2);
    });
  });
});
