import {expect, type Page, test} from '@playwright/test';

import {mockApis} from './support/dossier-mocks';

// The dossier's interactive price chart, against synthetic bars (support/dossier-mocks.ts): a real
// lightweight-charts canvas in a real browser, so the controls, the readouts and the measure tool are
// exercised end to end. The bar source is route-mocked; nothing here is real market data.

const DRAG_FROM = 0.3;
const DRAG_TO = 0.7;

async function openChart(page: Page, symbol = 'AAPL'): Promise<void> {
  await mockApis(page);
  await page.goto(`/assets/${symbol}`);
  await expect(page.getByRole('heading', {name: symbol, level: 1})).toBeVisible();
}

async function chartBox(page: Page) {
  const canvas = page.getByTestId('price-chart-canvas');
  await expect(canvas.locator('canvas').first()).toBeVisible();
  // The chart sits below the fold: the mouse only reaches it once it is scrolled into view.
  await canvas.scrollIntoViewIfNeeded();
  const box = await canvas.boundingBox();
  if (!box) {
    throw new Error('the chart has no box');
  }
  return box;
}

test.describe('Interactive price chart', () => {
  test('draws the chart with its readouts, the 52-week range and the volume pane', async ({
    page,
  }) => {
    await openChart(page);
    await chartBox(page);

    await expect(page.getByTestId('interactive-price-chart')).toBeVisible();
    await expect(page.getByTestId('chart-legend')).toContainText('O $');
    await expect(page.getByTestId('chart-legend')).toContainText('Vol ');
    await expect(page.getByTestId('visible-change')).toContainText('over');
    await expect(page.getByTestId('chart-week-range')).toHaveText(
      /^52-week range \$\d+\.\d{2} - \$\d+\.\d{2}$/
    );
    await expect(page.getByTestId('price-chart-canvas')).toHaveAttribute(
      'aria-label',
      /AAPL price chart, 1Y in Day candles/
    );
  });

  test('a period asks for its own bars, and only the candle sizes it can honestly draw stay on', async ({
    page,
  }) => {
    await openChart(page);
    await chartBox(page);
    const requests: string[] = [];
    page.on('request', req => {
      if (req.url().includes('/bars')) {
        requests.push(new URL(req.url()).search);
      }
    });

    const intervals = page.getByTestId('chart-intervals');
    await expect(intervals.getByRole('button', {name: '5m', exact: true})).toBeDisabled();
    await expect(intervals.getByRole('button', {name: 'Day'})).toBeEnabled();

    await page.getByTestId('chart-periods').getByRole('button', {name: '1D'}).click();

    await expect.poll(() => requests.at(-1)).toBe('?period=1D');
    await expect(intervals.getByRole('button', {name: '5m', exact: true})).toBeEnabled();
    await expect(intervals.getByRole('button', {name: 'Month'})).toBeDisabled();
    await expect(page.getByTestId('chart-notes')).toContainText(
      'minute bars for the recent past only'
    );

    await intervals.getByRole('button', {name: '15m', exact: true}).click();
    await expect.poll(() => requests.at(-1)).toBe('?period=1D&interval=15m');
  });

  test('the chart type changes without another request', async ({page}) => {
    await openChart(page);
    await chartBox(page);
    let requested = 0;
    page.on('request', req => {
      requested += req.url().includes('/bars') ? 1 : 0;
    });

    await page.getByTestId('chart-kind').getByRole('radio', {name: 'Area'}).click();
    await page.getByTestId('chart-kind').getByRole('radio', {name: 'Line'}).click();

    await expect(page.getByTestId('price-chart-canvas').locator('canvas').first()).toBeVisible();
    expect(requested).toBe(0);
  });

  test('moving averages toggle on, say what they need, and stay on at the next visit', async ({
    page,
  }) => {
    await openChart(page);
    await chartBox(page);
    const averages = page.getByTestId('chart-averages');

    await averages.getByRole('button', {name: 'SMA 50'}).click();
    await averages.getByRole('button', {name: 'EMA 9'}).click();
    await expect(page.getByTestId('chart-average-key')).toHaveText([/SMA 50/, /EMA 9/]);

    await page.reload();
    await chartBox(page);

    await expect(averages.getByRole('button', {name: 'SMA 50'})).toHaveAttribute(
      'aria-pressed',
      'true'
    );
    await expect(averages.getByRole('button', {name: 'SMA 200'})).toHaveAttribute(
      'aria-pressed',
      'false'
    );
    await expect(page.getByTestId('chart-average-key')).toHaveCount(2);

    await averages.getByRole('button', {name: 'SMA 50'}).click();
    await averages.getByRole('button', {name: 'EMA 9'}).click();
    await page.reload();
    await chartBox(page);
    await expect(page.getByTestId('chart-average-key')).toHaveCount(0);
  });

  test('Shift + drag measures a span: change in $, % and days', async ({page}) => {
    await openChart(page);
    const box = await chartBox(page);
    const y = box.y + box.height * 0.4;

    await page.keyboard.down('Shift');
    await page.mouse.move(box.x + box.width * DRAG_FROM, y);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width * DRAG_TO, y, {steps: 8});
    await page.mouse.up();
    await page.keyboard.up('Shift');

    const readout = page.getByTestId('measure-readout');
    await expect(readout).toContainText('Measured');
    await expect(readout).toContainText(/[+-]\$\d/);
    await expect(readout).toContainText(/%/);
    await expect(readout).toContainText(/days/);
    await expect(page.getByTestId('price-chart-selection')).toBeVisible();

    await readout.getByRole('button', {name: 'Clear'}).click();
    await expect(readout).toBeHidden();
    await expect(page.getByTestId('price-chart-selection')).toBeHidden();
  });

  test('the visible-range readout recomputes when the chart is zoomed', async ({page}) => {
    await openChart(page);
    const box = await chartBox(page);
    const before = await page.getByTestId('visible-change').innerText();

    await page.mouse.move(box.x + box.width / 2, box.y + box.height * 0.4);
    await page.mouse.wheel(0, -600);

    await expect.poll(() => page.getByTestId('visible-change').innerText()).not.toBe(before);
  });

  test('a symbol with no price history says so instead of drawing an empty chart', async ({
    page,
  }) => {
    await openChart(page, 'ZZZZ');

    // The dossier for an unknown symbol has no performance block, so the chart card is not offered at all.
    await expect(page.getByTestId('interactive-price-chart')).toHaveCount(0);
  });
});

test.describe('Interactive price chart on a phone', () => {
  test.use({viewport: {width: 375, height: 800}, hasTouch: true, isMobile: true});

  test('stays inside the screen, and a touch drag measures once Measure is on', async ({page}) => {
    await openChart(page);
    const box = await chartBox(page);
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(375);
    // The window the period names is what is in view, not the warm-up behind it.
    await expect(page.getByTestId('visible-change')).toContainText('over 365 days');
    const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(scrollWidth).toBeLessThanOrEqual(375);

    await page.getByTestId('chart-measure-toggle').getByRole('button', {name: 'Measure'}).click();
    await page.getByTestId('price-chart-canvas').scrollIntoViewIfNeeded();
    const live = await chartBox(page);
    const y = live.y + live.height * 0.4;

    const client = await page.context().newCDPSession(page);
    const touch = (type: 'touchStart' | 'touchMove' | 'touchEnd', x?: number) =>
      client.send('Input.dispatchTouchEvent', {
        type,
        touchPoints: x === undefined ? [] : [{x, y}],
      });
    await touch('touchStart', live.x + live.width * DRAG_FROM);
    for (const step of [0.4, 0.5, 0.6, DRAG_TO]) {
      await touch('touchMove', live.x + live.width * step);
    }
    await touch('touchEnd');

    await expect(page.getByTestId('measure-readout')).toContainText(/days/);
    await expect(page.getByTestId('price-chart-selection')).toBeVisible();
  });
});
