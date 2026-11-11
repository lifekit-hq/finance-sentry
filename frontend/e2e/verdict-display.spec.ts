import {expect, test} from '@playwright/test';

import {INSTITUTION_ROW} from './support/accounts-mocks';
import {API, mockApis} from './support/dossier-mocks';
import {mockPreferences} from './support/preferences-mocks';

const PHONE = {width: 375, height: 812};
const DESKTOP = {width: 1280, height: 900};
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
      `Turns BUY when ${TURNS_BUY}.`
    );
  });
});
