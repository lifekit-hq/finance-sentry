import {expect, test} from '@playwright/test';

import {API, mockApis} from './support/dossier-mocks';

const SCREEN = {
  archetype: 'growth',
  archetypes: [
    {
      key: 'balanced',
      label: 'Balanced',
      description: 'A wider book of sound-fundamentals names.',
      eligibleVerdicts: ['StrongBuy', 'Buy'],
      minStructure: 50,
      minFundamentals: 60,
      excludeExtended: true,
      ordering: ['fundamentals', 'structure'],
    },
    {
      key: 'growth',
      label: 'Growth',
      description: 'A concentrated book of the strongest-structure names.',
      eligibleVerdicts: ['StrongBuy', 'Buy'],
      minStructure: 60,
      minFundamentals: null,
      excludeExtended: true,
      ordering: ['structure', 'fundamentals'],
    },
  ],
  generatedAt: '2026-11-05T09:00:00Z',
  universeCap: 40,
  truncated: false,
  construction: {
    constraints: [
      {
        key: 'maxPositionPct',
        label: 'Largest single position',
        value: 12,
        source: 'archetype default',
      },
      {key: 'cashFloorPct', label: 'Cash floor', value: 5, source: 'archetype default'},
    ],
    sleeves: [
      {assetClass: 'Equities', weightPct: 12, builtFromUniverse: true, note: null},
      {assetClass: 'Cash', weightPct: 88, builtFromUniverse: false, note: null},
    ],
    cashPct: 88,
    notes: [],
    refusal: null,
    namesHeld: 1,
  },
  rows: [
    {
      symbol: 'AAA',
      sources: ['curated'],
      failureReason: null,
      scoredAt: '2026-11-05T09:00:00Z',
      verdict: {
        word: 'Buy',
        rulesVersion: 1,
        firedRuleId: 'buy',
        firedRule: 'Structure and fundamentals clear the BUY bars.',
      },
      structure: {raw: 64, adjusted: 68, basis: 'adjusted', notEvaluable: []},
      fundamentals: {score: 72, notEvaluable: []},
      crowding: {class: 'Normal', extensionFromMa50: 0.04},
      valuation: {
        notApplicable: false,
        trailingPe: 28,
        trailingPeFiveYearAvg: 24,
        premiumPct: 16.7,
        impliedUpsidePct: 9.1,
        notEvaluable: null,
        stale: false,
      },
      constructionRank: 1,
      suggestedWeightPct: 12,
      reason:
        'Passed the Growth gates with BUY; ranked #1; 12% (equal weight, capped at 12% (archetype default)).',
    },
    {
      symbol: 'BBB',
      sources: ['watchlist'],
      failureReason: null,
      scoredAt: '2026-11-05T09:00:00Z',
      verdict: {
        word: 'Wait',
        rulesVersion: 1,
        firedRuleId: 'otherwise',
        firedRule: 'No earlier rule matched.',
      },
      structure: {raw: 55, adjusted: 55, basis: 'raw', notEvaluable: []},
      fundamentals: {score: null, notEvaluable: ['too_few_periods']},
      crowding: {class: 'Normal', extensionFromMa50: 0.01},
      valuation: {
        notApplicable: false,
        trailingPe: null,
        trailingPeFiveYearAvg: null,
        premiumPct: null,
        impliedUpsidePct: null,
        notEvaluable: 'no_pe',
        stale: false,
      },
      constructionRank: null,
      suggestedWeightPct: null,
      reason: 'The verdict is WAIT, which Growth does not build from (STRONG BUY, BUY).',
    },
  ],
};

const GAP = {
  archetype: 'growth',
  generatedAt: '2026-11-14T08:00:00Z',
  label:
    'Informational only: what you hold against a rules-built target. It is not a recommendation to buy or sell, and nothing here places or prepares a trade.',
  hasBook: true,
  bookValueUsd: 10000,
  bookStale: false,
  staleSources: [],
  refusal: null,
  summary: {
    headline: 'Missing: BBB. Overweight: ZZZ.',
    missingNames: ['BBB'],
    underweightNames: [],
    overweightNames: ['ZZZ'],
    onTargetCount: 1,
  },
  sleeves: [
    {
      assetClass: 'Equities',
      status: 'Overweight',
      currentPct: 17,
      targetPct: 12,
      gapPct: -5,
      currentUsd: 1700,
      targetUsd: 1200,
      gapUsd: -500,
    },
  ],
  sectors: [],
  names: [
    {
      symbol: 'BBB',
      sector: 'Energy',
      verdict: 'Buy',
      status: 'Missing',
      heldOutsideTarget: false,
      currentPct: 0,
      targetPct: 12,
      gapPct: 12,
      currentUsd: 0,
      targetUsd: 1200,
      gapUsd: 1200,
      reason: 'Target 12%. Not held.',
    },
    {
      symbol: 'ZZZ',
      sector: null,
      verdict: 'Avoid',
      status: 'Overweight',
      heldOutsideTarget: true,
      currentPct: 5,
      targetPct: 0,
      gapPct: -5,
      currentUsd: 500,
      targetUsd: 0,
      gapUsd: -500,
      reason: 'Held outside the target: 5% of the book.',
    },
  ],
  notes: [],
};

test.describe('Construction', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
    await page.route(`${API}/portfolio-construction/universe*`, route =>
      route.fulfill({status: 200, contentType: 'application/json', body: JSON.stringify(SCREEN)})
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
    await page.getByTestId('universe-symbol').click();
    await expect(page).toHaveURL(/\/assets\/BBB/);
  });
});
