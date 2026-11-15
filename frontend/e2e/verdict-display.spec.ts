import {expect, type Page, test} from '@playwright/test';

import {INSTITUTION_ROW} from './support/accounts-mocks';
import {API, DOSSIER_AAPL, mockApis} from './support/dossier-mocks';
import {mockPreferences} from './support/preferences-mocks';

const PHONE = {width: 375, height: 812};
const DESKTOP = {width: 1280, height: 900};
const GAP_LINES = {
  no_role: 'No verdict for your role.',
  outside_universe: 'Outside the scoring universe (crypto / fund type).',
  unreadable: 'The verdict could not be read, retry.',
} as const;
const TURNS_BUY = 'structure reaches 50 (reads 39)';

test.describe('Verdict display', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('opens simple, switches to the numbered trail and keeps the choice across a reload', async ({
    page,
  }) => {
    const {patches} = await mockPreferences(page);
    await page.goto('/assets/AAPL');

    const card = page.getByTestId('dossier-verdict');
    await expect(card.getByTestId('verdict-reasons')).toBeVisible();
    await expect(card.getByTestId('verdict-trail')).toHaveCount(0);

    await card.getByRole('radio', {name: 'Detailed'}).click();

    await expect(card.getByTestId('verdict-trail')).toBeVisible();
    await expect(card.getByTestId('verdict-reasons')).toHaveCount(0);
    await expect(card.getByTestId('verdict-trail-step-1')).toBeVisible();
    await expect(card.getByTestId('verdict-trail-step-3')).toHaveAttribute('data-fired', 'true');
    expect(patches).toEqual([{verdictDisplay: 'detailed'}]);

    await page.reload();

    await expect(page.getByTestId('dossier-verdict').getByTestId('verdict-trail')).toBeVisible();
    await expect(page.getByRole('radio', {name: 'Detailed'})).toBeChecked();
  });

  test('starts detailed when the account says so, and goes back to simple', async ({page}) => {
    const {patches} = await mockPreferences(
      page,
      {theme: 'system', seed: null, intensity: null},
      'detailed'
    );
    await page.goto('/assets/AAPL');
    const card = page.getByTestId('dossier-verdict');
    await expect(card.getByTestId('verdict-trail')).toBeVisible();

    await card.getByRole('radio', {name: 'Simple'}).click();

    await expect(card.getByTestId('verdict-reasons')).toBeVisible();
    await expect(card.getByTestId('verdict-trail')).toHaveCount(0);
    expect(patches).toEqual([{verdictDisplay: 'simple'}]);
  });

  for (const [name, viewport] of Object.entries({phone: PHONE, desktop: DESKTOP})) {
    test(`the detailed trail fits the ${name} without sideways scroll`, async ({page}) => {
      await mockPreferences(page, {theme: 'system', seed: null, intensity: null}, 'detailed');
      await page.setViewportSize(viewport);
      await page.goto('/assets/AAPL');
      await expect(page.getByTestId('verdict-trail')).toBeVisible();

      // The app shell scrolls inside its own container, so judge the trail itself: it ends inside the viewport
      // and no step is wider than the trail that holds it.
      const trail = await page.getByTestId('verdict-trail').boundingBox();
      expect(trail).not.toBeNull();
      expect((trail?.x ?? 0) + (trail?.width ?? 0)).toBeLessThanOrEqual(viewport.width);
      const steps = await page
        .locator('[data-testid^="verdict-trail-step-"]')
        .evaluateAll(items => items.map(item => item.scrollWidth - item.clientWidth));
      expect(Math.max(...steps)).toBeLessThanOrEqual(0);
    });
  }

  test('an Accounts row says what would turn its verdict BUY, where the API names a bar', async ({
    page,
  }) => {
    await page.route(`${API}/research/scorecards/verdicts**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {symbol: 'AAPL', word: 'Wait', firedRuleId: 'otherwise', turnsBuyWhen: TURNS_BUY},
        ]),
      })
    );
    await page.goto('/accounts/list');
    const broker = page.locator(INSTITUTION_ROW, {hasText: 'Interactive Brokers'});
    await broker.locator('button[aria-controls]').first().click();

    const row = broker.getByTestId('position-row').filter({hasText: 'AAPL'});
    await expect(row.getByTestId('position-verdict')).toContainText('WAIT');
    await expect(row.getByTestId('position-verdict-note')).toHaveText(
      `The word reads BUY when ${TURNS_BUY}.`
    );
  });

  const mockDossierWithGap = async (page: Page, scorecardGap: keyof typeof GAP_LINES) =>
    page.route(`${API}/research/assets/AAPL/dossier`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({...DOSSIER_AAPL, scorecard: null, scorecardGap}),
      })
    );

  for (const reason of ['no_role', 'outside_universe'] as const) {
    test(`a dossier with no verdict says why (${reason}), with no retry`, async ({page}) => {
      await mockDossierWithGap(page, reason);
      await page.goto('/assets/AAPL');

      const card = page.getByTestId('dossier-verdict-gap');
      await expect(card).toContainText(GAP_LINES[reason]);
      await expect(card.getByRole('button', {name: /retry/i})).toHaveCount(0);
    });
  }

  test('an unreadable verdict offers a retry that reads it again', async ({page}) => {
    await mockDossierWithGap(page, 'unreadable');
    await page.route(`${API}/research/scorecards/AAPL`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(DOSSIER_AAPL.scorecard),
      })
    );
    await page.goto('/assets/AAPL');

    const card = page.getByTestId('dossier-verdict-gap');
    await expect(card).toContainText(GAP_LINES.unreadable);
    await card.getByRole('button', {name: /retry/i}).click();

    await expect(page.getByTestId('dossier-verdict')).toBeVisible();
    await expect(page.getByTestId('dossier-verdict-gap')).toHaveCount(0);
  });

  test('an Accounts row shows a dash with the reason on hover where no verdict is read', async ({
    page,
  }) => {
    await page.route(`${API}/research/scorecards/verdicts**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            symbol: 'AAPL',
            word: null,
            firedRuleId: null,
            turnsBuyWhen: null,
            gapReason: 'unreadable',
          },
        ]),
      })
    );
    await page.goto('/accounts/list');
    const broker = page.locator(INSTITUTION_ROW, {hasText: 'Interactive Brokers'});
    await broker.locator('button[aria-controls]').first().click();

    const row = broker.getByTestId('position-row').filter({hasText: 'AAPL'});
    const chip = row.getByTestId('verdict-chip-gap');
    await expect(chip).toHaveText('—');
    await expect(chip).toHaveAttribute('title', new RegExp(GAP_LINES.unreadable));
  });

  // The verdict chip sits beside the ticker and the value column is wide: neither may squeeze the
  // symbol (it once truncated to nothing at 390px), so the chip or the column wraps first.
  for (const width of [375, 390]) {
    test(`a verdict chip never truncates the ticker at ${width}px`, async ({page}) => {
      await page.setViewportSize({width, height: 844});
      await page.route(`${API}/research/scorecards/verdicts**`, route =>
        route.fulfill({
          status: 200,
          body: JSON.stringify([
            {symbol: 'AAPL', word: 'Wait', firedRuleId: 'otherwise', turnsBuyWhen: null},
          ]),
        })
      );
      await page.goto('/accounts/list');
      const broker = page.locator(INSTITUTION_ROW, {hasText: 'Interactive Brokers'});
      await broker.locator('button[aria-controls]').first().click();

      const row = broker.getByTestId('position-row').filter({hasText: 'AAPL'});
      await expect(row.getByTestId('position-verdict')).toBeVisible();
      const overflow = await row
        .getByTestId('position-ticker')
        .evaluate(ticker => ticker.scrollWidth - ticker.clientWidth);
      expect(overflow).toBeLessThanOrEqual(0);
      await expect(row.getByTestId('position-ticker')).toHaveText('AAPL');
      expect(
        await row.getByTestId('position-ticker').evaluate(ticker => ticker.clientWidth)
      ).toBeGreaterThan(0);
    });
  }
});
