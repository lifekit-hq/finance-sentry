import {expect, type Page, test} from '@playwright/test';

import {API} from './support/dossier-mocks';
import {mockHome, NET_WORTH_BRIDGE} from './support/home-mocks';
import {json, mockGet} from './support/visual-baseline';

// The net-worth bridge (finance-framework P4, M5) in two places. Home reads it as three lines under the
// hero (you saved, markets, exchange rates) and the Change total, following the range chips; Breakdown
// draws the full waterfall from the start value to the end through each line, always shows the unexplained
// residual, and says in place when the residual is above tolerance.
const PHONE = {width: 390, height: 1500};

const DEGRADED_BRIDGE = {
  ...NET_WORTH_BRIDGE,
  savings: 80,
  residual: 250,
  residualAboveTolerance: true,
  quality: 'DEGRADED',
  reasons: [{code: 'RESIDUAL_ABOVE_TOLERANCE'}],
};

const UNAVAILABLE_BRIDGE = {
  ...NET_WORTH_BRIDGE,
  startValue: null,
  endValue: null,
  savings: null,
  investmentReturn: null,
  market: null,
  income: null,
  fees: null,
  taxes: null,
  fxTranslation: null,
  revaluation: null,
  newlyLinked: null,
  unlinked: null,
  residual: null,
  quality: 'UNAVAILABLE',
  reasons: [{code: 'SHORT_HISTORY'}],
};

async function open(page: Page, path: string, bridge: unknown): Promise<string[]> {
  const ranges: string[] = [];
  await mockHome(page);
  await mockGet(page, 'dashboard/flow-breakdown**', {month: '2026-09', items: []});
  await page.route(`${API}/net-worth/bridge**`, route => {
    ranges.push(new URL(route.request().url()).searchParams.get('from') ?? 'all');
    return route.fulfill(json(bridge));
  });
  await page.goto(path);
  return ranges;
}

const openHome = (page: Page, bridge: unknown = NET_WORTH_BRIDGE): Promise<string[]> =>
  open(page, '/dashboard', bridge);

const openBreakdown = (page: Page, bridge: unknown = NET_WORTH_BRIDGE): Promise<string[]> =>
  open(page, '/dashboard/breakdown', bridge);

const amountOf = (page: Page, key: string) =>
  page.getByTestId(`net-worth-why-moved-${key}`).getByTestId('net-worth-why-moved-amount');

test.describe('Home: why net worth moved', () => {
  test('reads the move as you saved, markets and exchange rates, after the range chips', async ({
    page,
  }) => {
    await openHome(page);

    const card = page.getByTestId('net-worth-why-moved');
    await expect(card.locator('h2')).toContainText('Why it moved');
    await expect(amountOf(page, 'savings')).toHaveText('+$300');
    await expect(amountOf(page, 'investmentReturn')).toHaveText('-$150');
    await expect(amountOf(page, 'fxTranslation')).toHaveText('-$80');
    await expect(page.getByTestId('net-worth-why-moved-fxTranslation')).toContainText(
      'Rates moved, so your accounts in other currencies are worth less in USD'
    );
    await expect(page.getByTestId('net-worth-why-moved-change')).toContainText('+$100');
    await expect(page.getByTestId('net-worth-why-moved-change-note')).toHaveText(
      'Includes +$30 from accounts linked or removed, revaluations and moves not yet explained.'
    );
    await expect(page.getByTestId('net-worth-bridge')).toHaveCount(0);

    const chips = page.locator('lk-chip-row[label="History range"]');
    const chipsBottom = (await chips.boundingBox())?.y ?? 0;
    expect((await card.boundingBox())?.y ?? 0).toBeGreaterThan(chipsBottom);
  });

  test('says why there is nothing to explain when the range holds too little history', async ({
    page,
  }) => {
    await openHome(page, UNAVAILABLE_BRIDGE);

    await expect(page.getByTestId('net-worth-why-moved-unavailable')).toHaveText(
      'There is not enough history yet to measure this period.'
    );
    await expect(page.getByTestId('net-worth-why-moved-change')).toHaveCount(0);
  });

  test('follows the hero range chips', async ({page}) => {
    const ranges = await openHome(page);
    await expect(page.getByTestId('net-worth-why-moved-change')).toBeVisible();
    const before = ranges.at(-1);

    await page
      .getByRole('group', {name: 'History range'})
      .getByRole('button', {name: '1Y', exact: true})
      .click();

    await expect.poll(() => ranges.at(-1)).not.toBe(before);
  });

  test('opens the full statement in Breakdown', async ({page}) => {
    await openHome(page);

    await page.getByTestId('net-worth-why-moved-details').click();

    await expect(page).toHaveURL(/\/dashboard\/breakdown/);
    await expect(page.getByTestId('net-worth-bridge-end')).toContainText('$50,000.00');
  });

  test('fits a 390 px phone without horizontal scroll', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openHome(page);

    const card = page.getByTestId('net-worth-why-moved');
    await expect(page.getByTestId('net-worth-why-moved-change')).toBeVisible();
    const box = await card.boundingBox();
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(PHONE.width);
    expect(await card.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  });
});

test.describe('Breakdown net-worth bridge', () => {
  test('walks from the start to the end through each line', async ({page}) => {
    await openBreakdown(page);

    const card = page.getByTestId('net-worth-bridge');
    await expect(card.locator('h2')).toHaveText('What moved net worth');
    await expect(page.getByTestId('net-worth-bridge-start')).toContainText('$49,900.00');
    await expect(page.getByTestId('net-worth-bridge-savings')).toContainText('+$300.00');
    await expect(page.getByTestId('net-worth-bridge-investmentReturn')).toContainText('-$150.00');
    await expect(page.getByTestId('net-worth-bridge-investmentReturn')).toContainText(
      'market -$120.00 · income +$10.00 · fees -$40.00'
    );
    await expect(page.getByTestId('net-worth-bridge-fxTranslation')).toContainText('-$80.00');
    await expect(page.getByTestId('net-worth-bridge-end')).toContainText('$50,000.00');
    await expect(page.getByTestId('net-worth-bridge-degraded')).toHaveCount(0);
  });

  test('always shows the unexplained residual, even within tolerance', async ({page}) => {
    await openBreakdown(page);

    const residual = page.getByTestId('net-worth-bridge-residual');
    await expect(residual).toContainText('Unexplained');
    await expect(residual).toContainText('+$30.00');
  });

  test('says in place why the bridge is degraded when the residual is above tolerance', async ({
    page,
  }) => {
    await openBreakdown(page, DEGRADED_BRIDGE);

    const notice = page.getByTestId('net-worth-bridge-degraded');
    await expect(notice).toContainText(
      'The change in net worth is not fully explained by known movements and price changes.'
    );
    await expect(notice).toContainText(
      '+$250.00 is unexplained, more than 0.2% of the starting value'
    );
    await expect(page.getByTestId('net-worth-bridge-residual')).toContainText('+$250.00');

    await page.getByTestId('net-worth-bridge-quality').getByTestId('headline-badge').click();
    await expect(page.getByTestId('headline-reasons')).toContainText(
      'The change in net worth is not fully explained by known movements and price changes.'
    );
  });

  test('says why there is no bridge when the range holds too little history', async ({page}) => {
    await openBreakdown(page, UNAVAILABLE_BRIDGE);

    await expect(page.getByTestId('net-worth-bridge-unavailable')).toHaveText(
      'There is not enough history yet to measure this period.'
    );
    await expect(page.getByTestId('net-worth-bridge-residual')).toHaveCount(0);
  });

  test('fits a 390 px phone without horizontal scroll', async ({page}) => {
    await page.setViewportSize(PHONE);
    await openBreakdown(page, DEGRADED_BRIDGE);

    const card = page.getByTestId('net-worth-bridge');
    await expect(page.getByTestId('net-worth-bridge-end')).toBeVisible();
    const box = await card.boundingBox();
    expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(PHONE.width);
    expect(await card.evaluate(el => el.scrollWidth <= el.clientWidth)).toBe(true);
  });
});
