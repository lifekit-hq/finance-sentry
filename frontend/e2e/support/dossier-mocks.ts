import type {Page} from '@playwright/test';

import {mockPreferences} from './preferences-mocks';

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

interface MockRule {
  key: string;
  label: string;
  passed: boolean | null;
  value: number | null;
  threshold: number | null;
  unit: 'percent' | 'multiple' | 'none';
  note?: string;
}

function rule(r: MockRule) {
  return {...r, note: r.note ?? null};
}

const NO_METRIC = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

export const DOSSIER_AAPL = {
  symbol: 'AAPL',
  position: {
    provider: 'ibkr',
    assetClass: 'Equities',
    quantity: 10,
    currentValueUsd: 17500,
    costBasisUsd: 15000,
    unrealizedPnlUsd: 2500,
    unrealizedPnlPercent: 16.67,
    shareOfBookPercent: 12.5,
    positionCapPercent: 10,
    returns: {
      unrealizedUsd: 2500,
      realizedUsd: 519,
      dividendsUsd: 48,
      currencyUsd: null,
      totalUsd: 3067,
      coverage: 'complete',
      averageCostNative: 150,
      costCurrency: 'USD',
      fillCount: 3,
    },
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
        value: 0.005,
        fiveYearAvg: 0.006,
        historyWindowYears: 5,
        historyUnavailable: false,
      },
    },
    consensusTarget: 210.0,
    impliedUpsidePct: 20.0,
    peerSet: {
      name: 'sector:Technology (default)',
      peers: [
        {ticker: 'MSFT', forwardPe: 31.4, evToEbitda: 22.8},
        {ticker: 'GOOGL', forwardPe: 21.7, evToEbitda: 14.2},
      ],
    },
    sources: ['yahoo_finance'],
    retrievedAt: '2026-09-01T08:00:00Z',
    name: 'Apple Inc.',
    sector: 'Technology',
    industry: 'Consumer Electronics',
    priceAsOf: '2026-09-01T07:55:00Z',
    targetLow: 160.0,
    targetHigh: 250.0,
    analystCount: 36,
    currency: 'USD',
    marketCap: 2.71e12,
  },
  performance: {
    lastClose: 175.0,
    yearChangePct: 22.2,
    high52Week: 178.6,
    low52Week: 141.3,
    fromHighPct: -2.0,
    benchmarkYearChangePct: 14.1,
    currency: 'USD',
    asOf: '2026-09-01',
  },
  checks: {
    axes: [
      {
        key: 'analysts',
        label: 'Analysts',
        passed: 2,
        evaluated: 2,
        rules: [
          rule({
            key: 'buy_share',
            label: 'Most analysts rate it Buy',
            passed: true,
            value: 83.3,
            threshold: 50,
            unit: 'percent',
          }),
          rule({
            key: 'target_upside',
            label: 'Analyst target above the price',
            passed: true,
            value: 20,
            threshold: 0,
            unit: 'percent',
          }),
        ],
      },
      {
        key: 'growth',
        label: 'Growth',
        passed: 1,
        evaluated: 2,
        rules: [
          rule({
            key: 'revenue_growth',
            label: 'Revenue grew over the last year',
            passed: true,
            value: 6.1,
            threshold: 0,
            unit: 'percent',
          }),
          rule({
            key: 'eps_growth',
            label: 'Earnings per share grew over the last year',
            passed: false,
            value: -2.4,
            threshold: 0,
            unit: 'percent',
          }),
        ],
      },
      {
        key: 'value',
        label: 'Value',
        passed: 0,
        evaluated: 2,
        rules: [
          rule({
            key: 'pe_vs_history',
            label: 'P/E below its own 5-year average',
            passed: false,
            value: 28.5,
            threshold: 25.3,
            unit: 'multiple',
          }),
          rule({
            key: 'forward_pe_vs_peers',
            label: 'Forward P/E below its peers',
            passed: false,
            value: 26.2,
            threshold: 26.6,
            unit: 'multiple',
            note: 'median of 2 peers',
          }),
        ],
      },
      {
        key: 'momentum',
        label: 'Momentum',
        passed: 2,
        evaluated: 2,
        rules: [
          rule({
            key: 'above_200d',
            label: 'Price above its 200-day average',
            passed: true,
            value: 175,
            threshold: 160.2,
            unit: 'none',
          }),
          rule({
            key: 'beats_sp500',
            label: 'Beat the S&P 500 over the year',
            passed: true,
            value: 22.2,
            threshold: 14.1,
            unit: 'percent',
          }),
        ],
      },
      {
        key: 'fit',
        label: 'Fit',
        passed: 1,
        evaluated: 2,
        rules: [
          rule({
            key: 'within_cap',
            label: 'Within the IPS position cap',
            passed: false,
            value: 12.5,
            threshold: 10,
            unit: 'percent',
          }),
          rule({
            key: 'has_thesis',
            label: 'Has an active thesis',
            passed: true,
            value: null,
            threshold: null,
            unit: 'none',
          }),
        ],
      },
    ],
  },
  userTarget: null,
  analysts: {
    recentActions: [
      {
        ticker: 'AAPL',
        firm: 'Goldman Sachs',
        actionType: 'TargetChange',
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
    {
      id: 'news-2',
      source: 'yahoo:AAPL',
      title: 'Apple supplier raises capacity for the next iPhone cycle',
      url: 'https://finance.yahoo.com/news/apple-supplier',
      summary: null,
      tickers: ['AAPL'],
      categories: [],
      publishedAt: '2026-08-14T09:30:00Z',
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
      severity: 'info',
      payload: {rsi: 35},
    },
    {
      timestamp: '2026-08-22T09:00:00Z',
      scanner: 'momentum',
      signalType: 'MACD_CROSSOVER',
      severity: 'notable',
      payload: {macd: 0.5},
    },
    {
      timestamp: '2026-08-28T09:00:00Z',
      scanner: 'radar',
      signalType: 'VOLUME_SPIKE',
      severity: 'alerted',
      payload: {volume: 2500000},
    },
  ],
  generatedAt: '2026-09-01T10:30:00Z',
};

// A crypto holding: no valuation, analysts or ownership; momentum and fit are the only checks.
export const DOSSIER_BTC = {
  symbol: 'BTC',
  position: {
    provider: 'binance',
    assetClass: 'Crypto',
    quantity: 0.5,
    currentValueUsd: 30000,
    costBasisUsd: 24000,
    unrealizedPnlUsd: 6000,
    unrealizedPnlPercent: 25,
    shareOfBookPercent: 8.4,
    positionCapPercent: 10,
    returns: null,
    taxLots: [],
  },
  thesis: null,
  valuation: {
    ticker: 'BTC',
    notApplicable: true,
    price: null,
    isStale: false,
    metrics: {
      trailingPe: NO_METRIC,
      forwardPe: NO_METRIC,
      evToEbitda: NO_METRIC,
      dividendYield: NO_METRIC,
    },
    consensusTarget: null,
    impliedUpsidePct: null,
    peerSet: null,
    sources: [],
    retrievedAt: '2026-09-01T08:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: null,
  },
  performance: {
    lastClose: 60000,
    yearChangePct: 41.5,
    high52Week: 68000,
    low52Week: 39000,
    fromHighPct: -11.8,
    benchmarkYearChangePct: 14.1,
    currency: 'USD',
    asOf: '2026-09-01',
  },
  checks: {
    axes: [
      {
        key: 'momentum',
        label: 'Momentum',
        passed: 2,
        evaluated: 2,
        rules: [
          rule({
            key: 'above_200d',
            label: 'Price above its 200-day average',
            passed: true,
            value: 60000,
            threshold: 52000,
            unit: 'none',
          }),
          rule({
            key: 'beats_sp500',
            label: 'Beat the S&P 500 over the year',
            passed: true,
            value: 41.5,
            threshold: 14.1,
            unit: 'percent',
          }),
        ],
      },
      {
        key: 'fit',
        label: 'Fit',
        passed: 1,
        evaluated: 1,
        rules: [
          rule({
            key: 'within_cap',
            label: 'Within the IPS position cap',
            passed: true,
            value: 8.4,
            threshold: 10,
            unit: 'percent',
          }),
          rule({
            key: 'has_thesis',
            label: 'Has an active thesis',
            passed: null,
            value: null,
            threshold: null,
            unit: 'none',
            note: 'no thesis on file',
          }),
        ],
      },
    ],
  },
  userTarget: {targetPrice: 75000, currency: 'USD', updatedAt: '2026-09-01T09:00:00Z'},
  analysts: null,
  recentNews: [],
  nextEarnings: null,
  radarSignals: [],
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

// The synthetic chart's shape: a drift with a few swings, the S&P 500 drifting slower.
const CHART_SHAPE = {
  dayMs: 86_400_000,
  days: 365,
  stepDays: 7,
  endUtc: Date.parse('2026-09-01T00:00:00Z'),
  waveCycles: 5,
  waveAmplitude: 0.04,
  benchmarkDrift: 0.141,
  benchmarkCycles: 3,
  benchmarkAmplitude: 0.015,
  digits: 2,
  percent: 100,
  isoDateLength: 10,
  trades: [
    {at: 8, side: 'buy', quantity: 6},
    {at: 30, side: 'buy', quantity: 6},
    {at: 44, side: 'sell', quantity: 2},
  ],
  averageCost: 150,
};

// A deterministic year of weekly closes ending on the dossier date, the S&P 500 rebased onto the first.
function chartFor(shape: {symbol: string; first: number; last: number; withTrades: boolean}) {
  const {symbol, first, last, withTrades} = shape;
  const range = '1Y';
  const c = CHART_SHAPE;
  const count = Math.floor(c.days / c.stepDays) + 1;
  const points = Array.from({length: count}, (_, i) => {
    const t = i / (count - 1);
    const wave = Math.sin(t * Math.PI * c.waveCycles) * c.waveAmplitude;
    const close = (first + (last - first) * t) * (1 + wave);
    const benchmark =
      first *
      (1 + c.benchmarkDrift * t + Math.sin(t * Math.PI * c.benchmarkCycles) * c.benchmarkAmplitude);
    const date = new Date(c.endUtc - (count - 1 - i) * c.stepDays * c.dayMs);
    return {
      date: date.toISOString().slice(0, c.isoDateLength),
      close: +close.toFixed(c.digits),
      benchmark: +benchmark.toFixed(c.digits),
    };
  });
  const firstClose = points[0].close;
  const lastClose = points[count - 1].close;
  return {
    symbol,
    range,
    currency: 'USD',
    points,
    benchmarkLabel: 'S&P 500',
    trades: withTrades
      ? c.trades.map(t => ({
          date: points[t.at].date,
          side: t.side,
          quantity: t.quantity,
          price: points[t.at].close,
          currency: 'USD',
        }))
      : [],
    averageCost: withTrades ? c.averageCost : null,
    changePct: +(((lastClose - firstClose) / firstClose) * c.percent).toFixed(c.digits),
    benchmarkChangePct: c.benchmarkDrift * c.percent,
    source: 'yahoo-chart',
    retrievedAt: '2026-09-01T08:00:00Z',
  };
}

// Synthetic OHLCV bars for the interactive chart: a drift with swings, weekdays only, ending on the dossier date.
const BARS_SHAPE = {
  endUtc: Date.parse('2026-09-01T00:00:00Z'),
  dayMs: 86_400_000,
  daySeconds: 86_400,
  history: 1_900,
  warmup: 200,
  intradayBars: 78,
  intradayStepSeconds: 300,
  sessionOpenSeconds: 48_600,
  msPerSecond: 1_000,
  saturday: 6,
  openRatio: 0.99,
  closeSkew: 1.7,
  halfSpread: 2,
  volumeSwing: 0.5,
  volumeSkew: 0.9,
  waveCycles: 9,
  waveAmplitude: 0.05,
  spread: 0.012,
  digits: 2,
  baseVolume: 1_500_000,
  periodDays: {
    '1M': 30,
    '3M': 91,
    '6M': 182,
    YTD: 243,
    '1Y': 365,
    '5Y': 1826,
    ALL: 1_900,
  } satisfies Record<string, number> as Record<string, number>,
  intervalsByPeriod: {
    '1D': ['5m', '15m', '1h'],
    '5D': ['15m', '1h', '1d'],
    '1M': ['1h', '1d'],
    '3M': ['1d', '1w'],
    '6M': ['1d', '1w'],
    YTD: ['1d', '1w'],
    '1Y': ['1d', '1w'],
    '5Y': ['1d', '1w', '1mo'],
    ALL: ['1w', '1mo'],
  } satisfies Record<string, string[]> as Record<string, string[]>,
  tradingDaysInYear: 252,
  isoDate: 10,
};

function barAt(index: number, first: number, last: number, total: number, time: number) {
  const b = BARS_SHAPE;
  const t = index / (total - 1);
  const mid =
    (first + (last - first) * t) * (1 + Math.sin(t * Math.PI * b.waveCycles) * b.waveAmplitude);
  const open = mid * (1 - b.spread * Math.sin(index));
  const close = mid * (1 + b.spread * Math.cos(index * b.closeSkew));
  const high = Math.max(open, close) * (1 + b.spread / b.halfSpread);
  const low = Math.min(open, close) * (1 - b.spread / b.halfSpread);
  const volume = Math.round(b.baseVolume * (1 + b.volumeSwing * Math.sin(index * b.volumeSkew)));
  const r = (n: number) => +n.toFixed(b.digits);
  return {time, open: r(open), high: r(high), low: r(low), close: r(close), volume};
}

function dailyBars(first: number, last: number) {
  const b = BARS_SHAPE;
  const days: number[] = [];
  for (let i = b.history; i >= 0; i--) {
    const at = b.endUtc - i * b.dayMs;
    const weekday = new Date(at).getUTCDay();
    if (weekday !== 0 && weekday !== b.saturday) {
      days.push(at / b.msPerSecond);
    }
  }
  return days.map((time, i) => barAt(i, first, last, days.length, time + b.sessionOpenSeconds));
}

// The shape of GET /research/assets/{symbol}/bars for the symbols the specs open; any other symbol has no history.
export function barsFor(
  symbol: string,
  period: string,
  interval: string | null
): Record<string, unknown> {
  const b = BARS_SHAPE;
  const shape =
    symbol === 'BTC'
      ? {first: 42400, last: 60000}
      : symbol === 'AAPL'
        ? {first: 143, last: 175}
        : null;
  const intervals = b.intervalsByPeriod[period] ?? b.intervalsByPeriod['1Y'];
  const chosen =
    interval && intervals.includes(interval)
      ? interval
      : intervals[0] === '5m'
        ? '5m'
        : intervals.includes('1d')
          ? '1d'
          : intervals[0];
  const base = {
    symbol,
    period,
    interval: chosen,
    intervals,
    currency: 'USD',
    timeZone: 'America/New_York',
    truncated: false,
    source: 'yahoo:chart',
    retrievedAt: '2026-09-01T08:00:00Z',
  };
  if (!shape) {
    return {...base, bars: [], windowStart: 0, weekRange: null};
  }
  const all = dailyBars(shape.first, shape.last);
  const lastYear = all.slice(-b.tradingDaysInYear);
  const weekRange = {
    high: Math.max(...lastYear.map(bar => bar.high)),
    low: Math.min(...lastYear.map(bar => bar.low)),
    from: new Date(lastYear[0].time * b.msPerSecond).toISOString().slice(0, b.isoDate),
    coversYear: true,
  };
  if (period === '1D') {
    const open = b.endUtc / b.msPerSecond + b.sessionOpenSeconds;
    const bars = Array.from({length: b.intradayBars}, (_, i) =>
      barAt(
        i,
        shape.last * b.openRatio,
        shape.last,
        b.intradayBars,
        open + i * b.intradayStepSeconds
      )
    );
    return {...base, bars, windowStart: bars[0].time, weekRange};
  }
  const lastTime = all[all.length - 1].time;
  const windowStart = lastTime - b.periodDays[period ?? '1Y'] * b.daySeconds;
  const firstIndex = Math.max(
    0,
    all.findIndex(bar => bar.time >= windowStart)
  );
  return {
    ...base,
    bars: all.slice(Math.max(0, firstIndex - b.warmup)),
    windowStart: all[firstIndex].time,
    weekRange,
  };
}

export const CHART_AAPL = chartFor({symbol: 'AAPL', first: 143, last: 175, withTrades: true});
export const CHART_BTC = chartFor({symbol: 'BTC', first: 42400, last: 60000, withTrades: false});

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
  await mockPreferences(page);
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
  await page.route(`${API}/research/assets/BTC/dossier`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(DOSSIER_BTC),
    })
  );
  // The range the page asks for comes back with the same year of closes, labelled with that range.
  await page.route(`${API}/research/assets/*/chart**`, route => {
    const url = new URL(route.request().url());
    const symbol = /assets\/([^/]+)\/chart/.exec(url.pathname)?.[1] ?? '';
    const range = url.searchParams.get('range') ?? '1Y';
    const chart = symbol === 'BTC' ? CHART_BTC : symbol === 'AAPL' ? CHART_AAPL : null;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        chart ? {...chart, range} : {...CHART_AAPL, symbol, points: [], trades: []}
      ),
    });
  });
  await page.route(`${API}/research/assets/*/bars**`, route => {
    const url = new URL(route.request().url());
    const symbol = /assets\/([^/]+)\/bars/.exec(url.pathname)?.[1] ?? '';
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(
        barsFor(symbol, url.searchParams.get('period') ?? '1Y', url.searchParams.get('interval'))
      ),
    });
  });
  await page.route(`${API}/research/assets/*/target`, route => {
    if (route.request().method() === 'DELETE') {
      return route.fulfill({status: 204});
    }
    const body = route.request().postDataJSON() as {targetPrice: number; currency: string | null};
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        targetPrice: body.targetPrice,
        currency: body.currency ?? 'USD',
        updatedAt: '2026-09-01T12:00:00Z',
      }),
    });
  });
  // AAPL has a live quote; any other ticker has none, so its header keeps the empty reserved slot.
  await page.route(`${API}/research/quotes**`, route =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(route.request().url().includes('tickers=AAPL') ? [QUOTE_AAPL] : []),
    })
  );
  // Default: nothing generated yet. Individual tests re-route to cover the cached/stale/error paths.
  for (const symbol of ['AAPL', 'BTC']) {
    await page.route(`${API}/research/assets/${symbol}/narrative**`, route =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({...EMPTY_LEDGER_READ, symbol}),
      })
    );
  }
}
