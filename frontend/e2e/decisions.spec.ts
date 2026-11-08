import {expect, type Page, type Request, test} from '@playwright/test';

import {API, DOSSIER_AAPL, mockApis} from './support/dossier-mocks';
import {
  DECIDED,
  mockRecommendations,
  PENDING_AAPL,
  PENDING_DRAM,
} from './support/recommendation-mocks';
import {mockShell} from './support/visual-baseline';

// Ledger proposes, the owner decides (#1069). Confirming only records the decision: the one write the
// page sends is the verdict to Finance Sentry, and the order text is for the owner to place at IBKR.

/** Every write the page sends, so a test can prove the decision is the only one. */
function trackWrites(page: Page): Request[] {
  const writes: Request[] = [];
  page.on('request', request => {
    if (request.method() !== 'GET' && request.url().includes('/api/v1/')) {
      writes.push(request);
    }
  });
  return writes;
}

async function routeVerdict(page: Page, verdict: 'confirm' | 'reject'): Promise<void> {
  await page.route(`${API}/research/recommendations/*/${verdict}`, route => {
    const id = /recommendations\/([^/]+)\//.exec(route.request().url())?.[1];
    const pending = [PENDING_DRAM, PENDING_AAPL].find(rec => rec.id === id);
    const body = route.request().postDataJSON() as {note?: string | null; reason?: string};
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        ...pending,
        status: verdict === 'confirm' ? 'Confirmed' : 'Rejected',
        decidedAt: '2026-09-15T12:00:00Z',
        decisionNote: body.note ?? body.reason ?? null,
      }),
    });
  });
}

test.describe('Decisions on the asset dossier', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/research/assets/AAPL/dossier`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({...DOSSIER_AAPL, pendingRecommendation: PENDING_AAPL}),
      })
    );
    await routeVerdict(page, 'confirm');
  });

  test('confirming records the decision and sends nothing toward a broker', async ({page}) => {
    const writes = trackWrites(page);
    await page.goto('/assets/AAPL');

    const card = page.getByTestId('recommendation-card-AAPL');
    await expect(card).toContainText('Add');
    await expect(card.getByTestId('recommendation-size')).toContainText('10 shares');

    await card.getByTestId('recommendation-confirm').getByRole('button').click();
    const sheet = page.getByTestId('decision-sheet');
    await expect(sheet.getByTestId('decision-order-text')).toContainText('BUY 10 AAPL');
    await expect(sheet.getByTestId('decision-no-trade-wall')).toContainText(
      "Finance Sentry won't place this"
    );

    await sheet.getByTestId('decision-note').locator('textarea').fill('Topping up the sleeve.');
    await sheet.getByTestId('decision-submit').getByRole('button').click();

    await expect(page.getByTestId('dossier-decision-recorded')).toContainText(
      'Decision recorded: confirmed'
    );
    await expect(card).toBeHidden();
    expect(writes.map(w => `${w.method()} ${new URL(w.url()).pathname}`)).toEqual([
      'POST /api/v1/research/recommendations/rec-aapl/confirm',
    ]);
    expect(writes[0]?.postDataJSON()).toEqual({note: 'Topping up the sleeve.'});
  });
});

test.describe('Decisions queue in the Ledger tab', () => {
  test.beforeEach(async ({page}) => {
    await mockShell(page);
    await mockRecommendations(page);
    await routeVerdict(page, 'reject');
  });

  test('lists DRAM first, then the rest, as one-line rows with the last 30 days decided', async ({
    page,
  }) => {
    await page.goto('/ledger/decisions');
    await expect(page).toHaveURL(/\/ledger\/decisions$/);
    await expect(page.getByRole('navigation', {name: 'Ledger view'})).toBeVisible();

    const rows = page.locator('[data-testid^="decision-row-"]');
    await expect(rows).toHaveCount(2);
    await expect(rows.first()).toContainText('Trim');
    await expect(rows.first()).toContainText('DRAM');
    await expect(rows.first()).toContainText('$6,420');
    await expect(page.locator('fns-recommendation-card')).toHaveCount(0);
    // A row is one line: far shorter than the paragraph blocks it replaced.
    const box = await rows.first().boundingBox();
    expect(box?.height ?? Infinity).toBeLessThan(90);

    const decided = page.getByTestId('decisions-decided');
    for (const rec of DECIDED) {
      await expect(decided).toContainText(rec.ticker);
    }
  });

  test('a row pushes its detail, where Reject and Confirm stay on screen', async ({page}) => {
    await page.goto('/ledger/decisions');
    await page.getByTestId('decision-row-DRAM').getByRole('button').click();

    await expect(page).toHaveURL(/\/ledger\/decision\/rec-dram$/);
    const card = page.getByTestId('recommendation-card-DRAM');
    await expect(card).toContainText('Invalidated if');
    const actions = page.getByTestId('decision-actions');
    await expect(actions).toBeVisible();

    // Pinned: still inside the viewport after the record is scrolled to its end.
    await card.getByTestId('recommendation-evidence').scrollIntoViewIfNeeded();
    await expect(actions.getByTestId('recommendation-confirm')).toBeInViewport();
    await expect(actions.getByTestId('recommendation-reject')).toBeInViewport();
  });

  test('rejecting asks for the real reason and keeps it', async ({page}) => {
    const writes = trackWrites(page);
    await page.goto('/ledger/decision/rec-dram');

    await page
      .getByTestId('decision-actions')
      .getByTestId('recommendation-reject')
      .getByRole('button')
      .click();

    const sheet = page.getByTestId('decision-sheet');
    const submit = sheet.getByTestId('decision-submit').getByRole('button');
    await expect(submit).toBeDisabled();
    await sheet
      .getByTestId('decision-reason')
      .locator('textarea')
      .fill('Writing a thesis for DRAM this week.');
    await submit.click();

    await expect(sheet).toBeHidden();
    expect(writes.map(w => `${w.method()} ${new URL(w.url()).pathname}`)).toEqual([
      'POST /api/v1/research/recommendations/rec-dram/reject',
    ]);
    expect(writes[0]?.postDataJSON()).toEqual({reason: 'Writing a thesis for DRAM this week.'});
  });
});
