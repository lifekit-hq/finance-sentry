import {expect, type Page, type Request, test} from '@playwright/test';

import {API, DOSSIER_AAPL, mockApis} from './support/dossier-mocks';
import {mockRecommendations} from './support/recommendation-mocks';
import {
  mockThesisRevisions,
  REVISION_AAPL,
  REVISION_CBRS,
  REVISION_DECIDED,
} from './support/thesis-revision-mocks';
import {mockShell} from './support/visual-baseline';

// Ledger proposes a thesis change, only the owner confirms it (spec 050). Confirming writes the thesis and
// nothing else: the one write the page sends is the verdict to Finance Sentry, never a position.

function trackWrites(page: Page): Request[] {
  const writes: Request[] = [];
  page.on('request', request => {
    if (request.method() !== 'GET' && request.url().includes('/api/v1/')) {
      writes.push(request);
    }
  });
  return writes;
}

const written = (writes: Request[]): string[] =>
  writes.map(w => `${w.method()} ${new URL(w.url()).pathname}`);

test.describe('Thesis changes on the Decisions queue', () => {
  test.beforeEach(async ({page}) => {
    await mockShell(page);
    await mockRecommendations(page);
  });

  test('shows the change against the thesis on file, with the trigger diff and a way to decide', async ({
    page,
  }) => {
    await page.goto('/ledger/decisions');

    const card = page.getByTestId('thesis-revision-card-CBRS');
    await expect(card).toContainText('Change triggers');
    await expect(card.getByTestId('thesis-revision-why')).toContainText('no price-based exit');
    const diff = card.getByTestId('thesis-revision-diff');
    await expect(diff).toContainText('+ Price drawdown rises above 40.0%');
    await expect(diff).toContainText('Gross margin falls below 35.0%');
    await expect(card.getByTestId('thesis-revision-wall')).toContainText(
      'never places or changes a position'
    );
    await expect(card.getByTestId('thesis-revision-confirm')).toBeVisible();
    await expect(card.getByTestId('thesis-revision-reject')).toBeVisible();
    await expect(page.getByTestId('thesis-revisions-decided')).toContainText(
      REVISION_DECIDED.decisionNote
    );
  });

  test('rejecting asks for the real reason and sends only the verdict', async ({page}) => {
    await page.route(`${API}/research/thesis-revisions/rev-cbrs/reject`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          revision: {...REVISION_CBRS, status: 'Rejected', decisionNote: 'No price exit for me.'},
          thesis: null,
          gate: null,
          note: null,
        }),
      })
    );
    const writes = trackWrites(page);
    await page.goto('/ledger/decisions');

    await page
      .getByTestId('thesis-revision-card-CBRS')
      .getByTestId('thesis-revision-reject')
      .getByRole('button')
      .click();
    const sheet = page.getByTestId('thesis-revision-sheet');
    const submit = sheet.getByTestId('thesis-revision-submit').getByRole('button');
    await expect(submit).toBeDisabled();
    await sheet
      .getByTestId('thesis-revision-reason')
      .locator('textarea')
      .fill('No price exit for me.');
    await submit.click();

    await expect(sheet).toBeHidden();
    await expect(page.getByTestId('thesis-revision-card-CBRS')).toBeHidden();
    await expect(page.getByTestId('thesis-revisions-decided')).toContainText(
      'No price exit for me.'
    );
    expect(written(writes)).toEqual(['POST /api/v1/research/thesis-revisions/rev-cbrs/reject']);
    expect(writes[0]?.postDataJSON()).toEqual({reason: 'No price exit for me.'});
  });

  test('a confirm the risk gate refuses stays pending until the owner overrides', async ({
    page,
  }) => {
    const refusal = {
      revision: REVISION_CBRS,
      thesis: null,
      gate: {
        ruleKey: 'single_name_cap',
        observedValue: 0.14,
        limitValue: 0.1,
        maxCompliantSizeUsd: 1200,
        note: 'Above the single-name cap.',
      },
      note: null,
    };
    await page.route(`${API}/research/thesis-revisions/rev-cbrs/confirm`, route => {
      const body = route.request().postDataJSON() as {overrideRisk: boolean};
      return body.overrideRisk
        ? route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              revision: {...REVISION_CBRS, status: 'Confirmed'},
              thesis: null,
              gate: null,
              note: null,
            }),
          })
        : route.fulfill({
            status: 409,
            contentType: 'application/json',
            body: JSON.stringify(refusal),
          });
    });
    const writes = trackWrites(page);
    await page.goto('/ledger/decisions');

    await page
      .getByTestId('thesis-revision-card-CBRS')
      .getByTestId('thesis-revision-confirm')
      .getByRole('button')
      .click();
    const sheet = page.getByTestId('thesis-revision-sheet');
    await sheet.getByTestId('thesis-revision-submit').getByRole('button').click();

    await expect(sheet.getByTestId('thesis-revision-refusal')).toContainText(
      'Above the single-name cap.'
    );
    await expect(page.getByTestId('thesis-revision-card-CBRS')).toBeVisible();

    await sheet.getByTestId('thesis-revision-override').getByRole('checkbox').click();
    await sheet.getByTestId('thesis-revision-submit').getByRole('button').click();

    await expect(sheet).toBeHidden();
    await expect(page.getByTestId('thesis-revision-card-CBRS')).toBeHidden();
    expect(written(writes)).toEqual([
      'POST /api/v1/research/thesis-revisions/rev-cbrs/confirm',
      'POST /api/v1/research/thesis-revisions/rev-cbrs/confirm',
    ]);
    expect(writes.map(w => (w.postDataJSON() as {overrideRisk: boolean}).overrideRisk)).toEqual([
      false,
      true,
    ]);
  });

  test('a failed read shows the error with a retry, and never reads as empty', async ({page}) => {
    await page.route(`${API}/research/thesis-revisions`, route =>
      route.fulfill({status: 500, contentType: 'application/json', body: '{}'})
    );
    await page.goto('/ledger/decisions');

    await expect(page.getByTestId('thesis-revisions')).toContainText(
      'Could not load the thesis changes'
    );
    await expect(page.getByTestId('thesis-revisions-empty')).toBeHidden();
    // The recommendations above are untouched by it.
    await expect(page.getByTestId('decision-row-DRAM')).toBeVisible();
  });

  test('says so plainly when nothing was proposed', async ({page}) => {
    await mockThesisRevisions(page);
    await page.goto('/ledger/decisions');

    await expect(page.getByTestId('thesis-revisions-empty')).toContainText('No thesis changes yet');
  });
});

test.describe('Thesis change on the asset dossier', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await mockThesisRevisions(page, {pending: [REVISION_AAPL]});
  });

  test('sits under the hero against the thesis on the page, and confirming writes the thesis only', async ({
    page,
  }) => {
    const confirmed = {
      id: 'thesis-1',
      ...DOSSIER_AAPL.thesis,
      thesisText: REVISION_AAPL.proposed.thesisText,
    };
    await page.route(`${API}/research/thesis-revisions/rev-aapl/confirm`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          revision: {...REVISION_AAPL, status: 'Confirmed'},
          thesis: confirmed,
          gate: null,
          note: null,
        }),
      })
    );
    const writes = trackWrites(page);
    await page.goto('/assets/AAPL');

    const card = page.getByTestId('thesis-revision-card-AAPL');
    await expect(card.getByTestId('thesis-revision-before')).toContainText(
      'Apple continues to expand its services revenue and ecosystem lock-in.'
    );
    await expect(card.getByTestId('thesis-revision-after')).toContainText('28% of revenue');
    await expect(card.getByTestId('thesis-revision-evidence').getByRole('link')).toHaveAttribute(
      'href',
      'https://example.com/aapl'
    );

    await card.getByTestId('thesis-revision-confirm').getByRole('button').click();
    const sheet = page.getByTestId('thesis-revision-sheet');
    await expect(sheet.getByTestId('thesis-revision-sheet-wall')).toContainText(
      'never places or changes a position'
    );
    await sheet.getByTestId('thesis-revision-submit').getByRole('button').click();

    await expect(page.getByTestId('dossier-revision-recorded')).toContainText(
      'Thesis change confirmed'
    );
    await expect(card).toBeHidden();
    await expect(page.getByTestId('thesis-text')).toContainText('28% of revenue');
    expect(written(writes)).toEqual(['POST /api/v1/research/thesis-revisions/rev-aapl/confirm']);
    expect(writes[0]?.postDataJSON()).toEqual({note: null, overrideRisk: false});
  });
});
