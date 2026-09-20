import {expect, type Page, test} from '@playwright/test';

const API = '**/api/v1';
const WEALTH_SUMMARY = {
  totalNetWorth: 38500,
  baseCurrency: 'USD',
  appliedFilters: {category: null, provider: null},
  categories: [
    {
      category: 'brokerage',
      totalInBaseCurrency: 38500,
      institutionCount: 1,
      institutions: [
        {
          institutionId: 'ibkr-1',
          provider: 'ibkr',
          name: 'Interactive Brokers',
          category: 'brokerage',
          totalInBaseCurrency: 38500,
          syncStatus: 'synced',
          lastSyncTimestamp: '2026-09-01T10:00:00Z',
          lastSuccessfulSyncTimestamp: '2026-09-01T10:00:00Z',
          accounts: [
            {
              accountId: 'acc-aapl',
              bankName: 'Interactive Brokers',
              accountType: 'Stock',
              accountNumberLast4: 'AAPL',
              currency: 'USD',
              provider: 'ibkr',
              category: 'brokerage',
              currentBalance: 17500,
              balanceInBaseCurrency: 17500,
              syncStatus: 'synced',
              lastSyncTimestamp: '2026-09-01T10:00:00Z',
            },
          ],
          cards: null,
        },
      ],
    },
  ],
};

const AUTH_RESPONSE = {
  user: {id: 'test-user-id', email: 'test@gmail.com'},
  expiresAt: '2027-01-01T00:00:00Z',
};

const BROKERAGE_HOLDINGS = {
  provider: 'ibkr',
  syncedAt: '2026-09-01T10:00:00Z',
  isStale: false,
  positions: [
    {
      symbol: 'AAPL',
      instrumentType: 'STK',
      quantity: 10,
      usdValue: 17500,
      costBasisUsd: 15000,
      averageCostUsd: 1500,
    },
    {
      symbol: 'MSFT',
      instrumentType: 'STK',
      quantity: 5,
      usdValue: 21000,
      costBasisUsd: 18000,
      averageCostUsd: 3600,
    },
  ],
  totalUsdValue: 38500,
};

const CRYPTO_HOLDINGS = {
  provider: 'binance',
  syncedAt: '2026-09-01T10:00:00Z',
  isStale: false,
  holdings: [],
  totalUsdValue: 0,
};

const DOSSIER_AAPL = {
  symbol: 'AAPL',
  position: {
    provider: 'ibkr',
    quantity: 10,
    currentValueUsd: 17500,
    costBasisUsd: 15000,
    unrealizedPnlUsd: 2500,
    unrealizedPnlPercent: 16.67,
    taxLots: [
      {
        quantity: 10,
        currentValueUsd: 17500,
        averageCostUsd: 1500,
        costBasisUsd: 15000,
        unrealizedPnlUsd: 2500,
        unrealizedPnlPercent: 16.67,
        acquiredAt: '2024-03-15T00:00:00Z',
        isLongTerm: true,
      },
    ],
  },
  thesis: {
    id: 'thesis-1',
    ticker: 'AAPL',
    thesisText: 'Apple continues to expand its services revenue and ecosystem lock-in.',
    keyDataPoints: [],
    catalysts: [{date: '2026-10-15', event: 'Q4 Earnings expected strong services growth'}],
    invalidationTriggers: [
      {
        metric: 'services_revenue_growth',
        direction: 'below',
        threshold: 5,
        proxyTicker: null,
        consecutivePeriods: 2,
        periodType: 'Quarter',
      },
    ],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
    brokenAt: null,
    brokenReason: null,
    entryPrice: 175.0,
  },
  valuation: {
    ticker: 'AAPL',
    notApplicable: false,
    price: 175.0,
    isStale: false,
    metrics: {
      trailingPe: {
        value: 28.5,
        fiveYearAvg: 25.3,
        historyWindowYears: 5,
        historyUnavailable: false,
      },
      forwardPe: {value: 26.2, fiveYearAvg: 23.1, historyWindowYears: 5, historyUnavailable: false},
      evToEbitda: {
        value: null,
        fiveYearAvg: null,
        historyWindowYears: null,
        historyUnavailable: true,
      },
      dividendYield: {
        value: 0.5,
        fiveYearAvg: 0.6,
        historyWindowYears: 5,
        historyUnavailable: false,
      },
    },
    consensusTarget: 210.0,
    impliedUpsidePct: 20.0,
    peerSet: null,
    sources: ['yahoo_finance'],
    retrievedAt: '2026-09-01T08:00:00Z',
  },
  analysts: {
    recentActions: [
      {
        ticker: 'AAPL',
        firm: 'Goldman Sachs',
        actionType: 'Upgraded',
        priorRating: 'Neutral',
        newRating: 'Buy',
        priorTarget: 185.0,
        newTarget: 220.0,
        actionDate: '2026-08-20',
        source: 'benzinga',
        sourceUrl: null,
        ingestedAt: '2026-08-20T15:00:00Z',
      },
    ],
    trends: [
      {
        period: '0m',
        strongBuy: 18,
        buy: 12,
        hold: 5,
        sell: 1,
        strongSell: 0,
        source: 'yahoo_finance',
        ingestedAt: '2026-09-01T00:00:00Z',
      },
    ],
    coverage: 'inUniverse',
  },
  recentNews: [
    {
      id: 'news-1',
      source: 'Reuters',
      title: 'Apple reports record services revenue in Q3',
      url: 'https://reuters.com/apple-q3',
      summary: 'Apple beat analyst expectations with record services revenue.',
      tickers: ['AAPL'],
      categories: ['earnings'],
      publishedAt: '2026-08-15T12:00:00Z',
    },
  ],
  nextEarnings: {
    ticker: 'AAPL',
    eventType: 'Earnings Release',
    eventDate: '2026-10-15',
    isEstimate: true,
    source: 'yahoo_finance',
  },
  radarSignals: [
    {
      timestamp: '2026-08-15T09:00:00Z',
      scanner: 'momentum',
      signalType: 'RSI_OVERSOLD',
      severity: 'low',
      payload: {rsi: 35},
    },
    {
      timestamp: '2026-08-22T09:00:00Z',
      scanner: 'momentum',
      signalType: 'MACD_CROSSOVER',
      severity: 'medium',
      payload: {macd: 0.5},
    },
    {
      timestamp: '2026-08-28T09:00:00Z',
      scanner: 'radar',
      signalType: 'VOLUME_SPIKE',
      severity: 'high',
      payload: {volume: 2500000},
    },
  ],
  generatedAt: '2026-09-01T10:30:00Z',
};

// A symbol the backend knows nothing about: every section comes back null/empty.
const DOSSIER_UNKNOWN = {
  symbol: 'ZZZZ',
  position: null,
  thesis: null,
  valuation: null,
  analysts: null,
  recentNews: [],
  nextEarnings: null,
  radarSignals: [],
  generatedAt: '2026-09-01T10:30:00Z',
};

const EMPTY_LEDGER_READ = {
  symbol: 'AAPL',
  narrative: null,
  generatedAt: null,
  isStale: true,
  cached: false,
};

const LEDGER_READ_NARRATIVE =
  'You hold 50 AAPL at a 25% unrealised gain; the services thesis is intact.';

async function mockApis(page: Page): Promise<void> {
  await page.route(`${API}/auth/me`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  await page.route(`${API}/auth/refresh`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(AUTH_RESPONSE),
    })
  );
  await page.route(`${API}/brokerage/holdings`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(BROKERAGE_HOLDINGS),
    })
  );
  await page.route(`${API}/crypto/holdings`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(CRYPTO_HOLDINGS),
    })
  );
  await page.route(`${API}/wealth/summary`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(WEALTH_SUMMARY),
    })
  );
  await page.route(`${API}/research/assets/AAPL/dossier`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DOSSIER_AAPL),
    })
  );
  await page.route(`${API}/research/assets/ZZZZ/dossier`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DOSSIER_UNKNOWN),
    })
  );
  // Default: nothing generated yet. Individual tests re-route to cover the cached/stale/error paths.
  await page.route(`${API}/research/assets/AAPL/narrative**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(EMPTY_LEDGER_READ),
    })
  );
}

test.describe('Asset Dossier', () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('click holding symbol navigates to dossier page', async ({page}) => {
    await page.goto('/accounts/investments');
    // Wait for positions to load and AAPL to appear
    await expect(page.getByText('AAPL').first()).toBeVisible();

    // Click the AAPL symbol button
    await page.getByRole('button', {name: /AAPL/i}).first().click();

    // Should land on the dossier URL
    await expect(page).toHaveURL(/\/assets\/AAPL/);
  });

  test('dossier page renders symbol header', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();
  });

  test('dossier page renders position section', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Position')).toBeVisible();
    await expect(page.getByText('Current Value')).toBeVisible();
    // Unrealized P&L section
    await expect(page.getByText('Unrealized P&L')).toBeVisible();
  });

  test('dossier page renders thesis section', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Investment Thesis')).toBeVisible();
    await expect(page.getByText('Apple continues to expand its services revenue')).toBeVisible();
    await expect(page.getByText('Active')).toBeVisible();
  });

  test('dossier page renders analyst coverage', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Analyst Coverage')).toBeVisible();
    await expect(page.getByText('Goldman Sachs')).toBeVisible();
  });

  test('dossier page renders recent news', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recent News')).toBeVisible();
    await expect(page.getByText('Apple reports record services revenue in Q3')).toBeVisible();
  });

  test('back button returns to investments', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByRole('heading', {name: 'AAPL', level: 1})).toBeVisible();

    await page.getByRole('button', {name: /back to investments/i}).click();

    await expect(page).toHaveURL(/\/accounts\/investments/);
  });

  test('accounts list brokerage row navigates to dossier on click', async ({page}) => {
    await page.goto('/accounts/list');
    await expect(page.getByText('AAPL').first()).toBeVisible();

    await page.getByText('AAPL').first().click();

    await expect(page).toHaveURL(/\/assets\/AAPL/);
  });

  test('dossier page renders recommendation trend table', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Recommendation Trend')).toBeVisible();
    await expect(page.getByText('Strong Buy')).toBeVisible();
    // Verify a trend row is rendered
    await expect(page.getByRole('cell', {name: '18'})).toBeVisible();
  });

  test('dossier page renders radar sparkline SVG for multiple signals', async ({page}) => {
    await page.goto('/assets/AAPL');
    await expect(page.getByText('Radar Signals')).toBeVisible();
    // Sparkline SVG is rendered when there are >= 2 signals
    const sparkline = page.locator('svg[aria-hidden="true"]');
    await expect(sparkline).toBeVisible();
    // Latest reading is shown in the header
    await expect(page.getByText('Latest')).toBeVisible();
  });

  test('a symbol with no data on file renders a no-data state, not empty sections', async ({
    page,
  }) => {
    await page.goto('/assets/ZZZZ');

    await expect(page.getByRole('heading', {name: 'ZZZZ', level: 1})).toBeVisible();
    await expect(page.getByTestId('dossier-no-data')).toContainText('Nothing on file for ZZZZ');
    // No section card is rendered at all — not even a hollow one.
    await expect(page.getByText('Position', {exact: true})).toHaveCount(0);
    await expect(page.getByText('Investment Thesis')).toHaveCount(0);
    await expect(page.getByText('Radar Signals')).toHaveCount(0);
    await expect(page.getByTestId('ledger-read-card')).toHaveCount(0);
  });
});

test.describe("Ledger's read", () => {
  test.beforeEach(async ({page}) => {
    await mockApis(page);
  });

  test('offers to generate when nothing is cached', async ({page}) => {
    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-card')).toBeVisible();
    await expect(page.getByTestId('ledger-read-empty')).toBeVisible();
    await expect(page.getByTestId('ledger-read-generate')).toBeVisible();
    await expect(page.getByTestId('ledger-read-narrative')).toHaveCount(0);
    // The API reports a missing cache as stale — nothing was ever generated, so no flag.
    await expect(page.getByTestId('ledger-read-stale')).toHaveCount(0);
  });

  test('generate button posts to the agent and renders the returned read', async ({page}) => {
    let postCount = 0;
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        postCount += 1;
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            symbol: 'AAPL',
            narrative: LEDGER_READ_NARRATIVE,
            generatedAt: '2026-09-03T09:00:00Z',
            isStale: false,
            cached: false,
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(EMPTY_LEDGER_READ),
      });
    });

    await page.goto('/assets/AAPL');
    await page.getByTestId('ledger-read-generate').click();

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-regenerate')).toBeVisible();
    expect(postCount).toBe(1);
  });

  test('a cached read renders on load without regenerating', async ({page}) => {
    let postCount = 0;
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        postCount += 1;
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          symbol: 'AAPL',
          narrative: LEDGER_READ_NARRATIVE,
          generatedAt: '2026-09-03T09:00:00Z',
          isStale: false,
          cached: true,
        }),
      });
    });

    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-generated')).toBeVisible();
    await expect(page.getByTestId('ledger-read-stale')).toHaveCount(0);
    expect(postCount).toBe(0);
  });

  test('a stale cached read still renders, flagged out of date', async ({page}) => {
    await page.route(`${API}/research/assets/AAPL/narrative**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          symbol: 'AAPL',
          narrative: LEDGER_READ_NARRATIVE,
          generatedAt: '2026-08-01T09:00:00Z',
          isStale: true,
          cached: true,
        }),
      })
    );

    await page.goto('/assets/AAPL');

    await expect(page.getByTestId('ledger-read-narrative')).toContainText(LEDGER_READ_NARRATIVE);
    await expect(page.getByTestId('ledger-read-stale')).toBeVisible();
    await expect(page.getByTestId('ledger-read-regenerate')).toBeVisible();
  });

  test('surfaces a friendly message when the agent is unavailable', async ({page}) => {
    await page.route(`${API}/research/assets/AAPL/narrative**`, route => {
      if (route.request().method() === 'POST') {
        return route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({
            error: 'Ledger could not produce a read right now. Try again shortly.',
            errorCode: 'LEDGER_READ_UNAVAILABLE',
          }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(EMPTY_LEDGER_READ),
      });
    });

    await page.goto('/assets/AAPL');
    await page.getByTestId('ledger-read-generate').click();

    await expect(page.getByTestId('ledger-read-error')).toContainText(
      'Ledger could not produce a read right now.'
    );
  });
});
