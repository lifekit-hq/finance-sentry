import {expect, type Locator, test} from '@playwright/test';

import {API, BROKERAGE_HOLDINGS, mockApis} from './support/dossier-mocks';

const FIRST_SLICE_ANGLE_DEG = 20;
// The ring runs from about 70% of the canvas radius out to its rim; this lands midway.
const RING_MID_RADIUS_FRACTION = 0.85;

// The holdings donut has no legend or chrome, so its ring is centred in a square canvas. The
// canvas has no focusable items, so a click has to land on pixels.
async function clickRing(canvas: Locator, angleDeg: number): Promise<void> {
  const box = await canvas.boundingBox();
  if (!box) {
    throw new Error('donut canvas has no box');
  }
  const radius = (Math.min(box.width, box.height) / 2) * RING_MID_RADIUS_FRACTION;
  const angle = (angleDeg * Math.PI) / 180;
  await canvas
    .page()
    .mouse.click(
      box.x + box.width / 2 + radius * Math.sin(angle),
      box.y + box.height / 2 - radius * Math.cos(angle)
    );
}

test.describe('Holdings allocation donut click-through', () => {
  test("a class holding one position opens that position's dossier", async ({page}) => {
    await mockApis(page);
    const [aapl] = BROKERAGE_HOLDINGS.positions;
    await page.route(`${API}/brokerage/holdings`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          ...BROKERAGE_HOLDINGS,
          positions: [aapl],
          totalUsdValue: aapl.usdValue,
        }),
      })
    );
    await page.goto('/accounts/investments');
    const canvas = page.locator('cmn-donut-chart canvas');
    await expect(canvas).toBeVisible();

    await clickRing(canvas, FIRST_SLICE_ANGLE_DEG);

    await expect(page).toHaveURL(/\/assets\/AAPL/);
  });

  test('a class holding several positions has no single page, so a click stays put', async ({
    page,
  }) => {
    await mockApis(page);
    await page.goto('/accounts/investments');
    const canvas = page.locator('cmn-donut-chart canvas');
    await expect(canvas).toBeVisible();

    await clickRing(canvas, FIRST_SLICE_ANGLE_DEG);

    // Give a (wrong) navigation the chance to happen before asserting it did not.
    await page.waitForTimeout(500);
    await expect(page).toHaveURL(/\/accounts\/investments$/);
  });
});
