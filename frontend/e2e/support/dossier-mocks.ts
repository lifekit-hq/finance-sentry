import type {Page} from '@playwright/test';

export const API = '**/api/v1';
export const WEALTH_SUMMARY = {
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

export const AUTH_RESPONSE = {
  user: {
    id: 'test-user-id',
    email: 'test@gmail.com',
    roles: ['Owner'],
    permissions: [
      'connections.manage',
      'ai.use',
      'mcp.connect',
      'mcp.service',
      'ops.admin',
      'users.manage',
    ],
  },
  expiresAt: '2027-01-01T00:00:00Z',
};

export const BROKERAGE_HOLDINGS = {
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

export const CRYPTO_HOLDINGS = {
  provider: 'binance',
  syncedAt: '2026-09-01T10:00:00Z',
  isStale: false,
  holdings: [],
  totalUsdValue: 0,
};

export const DOSSIER_AAPL = {
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
        metric: 'revenue_yoy',
        direction: 'lessThan',
        threshold: 0.05,
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
        period: '2026-08-01',
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
export const DOSSIER_UNKNOWN = {
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

export const EMPTY_LEDGER_READ = {
  symbol: 'AAPL',
  narrative: null,
  generatedAt: null,
  isStale: true,
  cached: false,
};

export const QUOTE_AAPL = {
  ticker: 'AAPL',
  resolvedTicker: 'AAPL',
  price: 189.3,
  previousClose: 187,
  changePct: 1.2299,
  currency: 'USD',
  fetchedAt: '2026-09-01T12:00:00Z',
  marketState: 'REGULAR',
  session: 'regular',
  isStale: false,
  sourcePriceTime: null,
  regularMarketTime: null,
};

export const LEDGER_READ_NARRATIVE =
  'You hold 50 AAPL at a 25% unrealised gain; the services thesis is intact.';

export async function mockApis(page: Page): Promise<void> {
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
  // AAPL has a live quote; any other ticker has none, so its header keeps the empty reserved slot.
  await page.route(`${API}/research/quotes**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(route.request().url().includes('tickers=AAPL') ? [QUOTE_AAPL] : []),
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
