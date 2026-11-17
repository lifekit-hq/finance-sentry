import {expect, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';
import {GAP, UNIVERSE} from './support/screens/construction.screen';

test.describe('Construction', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/portfolio-construction/universe*`, route =>
      route.fulfill({status: 200, contentType: 'application/json', body: JSON.stringify(UNIVERSE)})
    );
    await page.route(`${API}/portfolio-construction/gap*`, route =>
      route.fulfill({status: 200, contentType: 'application/json', body: JSON.stringify(GAP)})
    );
  });

  test('shows the book against the target as missing and overweight, labelled informational', async ({
    page,
  }) => {
    await page.goto('/construction');
    await expect(page.getByTestId('gap-label')).toContainText('not a recommendation');
    await expect(page.getByTestId('gap-headline')).toHaveText('Missing: BBB. Overweight: ZZZ.');
    await expect(page.getByTestId('gap-text').first()).toHaveText('missing');
    await expect(page.getByTestId('gap-outside')).toHaveText('Held, not in target');
  });

  test('shows each name with its verdict, weight and the rule behind it', async ({page}) => {
    await page.goto('/construction');
    await expect(page.getByTestId('construction-not-advice')).toBeVisible();
    await expect(page.getByTestId('universe-row')).toHaveCount(2);
    await expect(page.getByTestId('universe-verdict').first()).toHaveText('BUY');
    await expect(page.getByTestId('universe-weight').first()).toHaveText('12%');
    await expect(page.getByTestId('universe-fundamentals').nth(1)).toContainText('no data');

    await page.getByTestId('universe-row').first().locator('summary').click();
    await expect(page.getByTestId('universe-rule').first()).toContainText('rules v1');
    await expect(page.getByTestId('universe-reason').first()).toContainText(
      'Passed the Growth gates'
    );
  });

  test('filters by verdict word and links the ticker to its dossier', async ({page}) => {
    await page.goto('/construction');
    await page.getByTestId('universe-filter-Wait').getByRole('button').click();
    await expect(page.getByTestId('universe-row')).toHaveCount(1);
    await page.getByTestId('universe-row').locator('summary').click();
    await page.getByTestId('universe-open-dossier').click();
    await expect(page).toHaveURL(/\/assets\/BBB/);
  });
});
