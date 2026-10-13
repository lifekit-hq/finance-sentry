import {describe, expect, it} from 'vitest';

import {OWNERSHIP_NOTE} from '../constants/dossier/dossier-facts.constants';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type DossierPerformanceDto,
  type DossierPositionSection,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {DossierFactsUtils} from './dossier-facts.utils';

const NO_METRIC = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

function metric(value: number) {
  return {...NO_METRIC, value, historyUnavailable: false};
}

function valuation(overrides: Partial<ValuationSnapshotDto> = {}): ValuationSnapshotDto {
  return {
    ticker: 'AAPL',
    notApplicable: false,
    price: 100,
    isStale: false,
    metrics: {
      trailingPe: metric(30.12),
      forwardPe: metric(28.04),
      evToEbitda: NO_METRIC,
      dividendYield: metric(0.0051),
    },
    consensusTarget: 120,
    impliedUpsidePct: 20,
    peerSet: null,
    sources: ['yahoo-quote', 'yahoo-summary', 'sec-edgar'],
    retrievedAt: '2026-10-12T00:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: '2026-10-13T00:00:00Z',
    currency: 'USD',
    marketCap: 3.12e12,
    ...overrides,
  };
}

const PERFORMANCE: DossierPerformanceDto = {
  lastClose: 100,
  yearChangePct: 22.24,
  high52Week: 102,
  low52Week: 70,
  fromHighPct: -1.96,
  benchmarkYearChangePct: 14.1,
  currency: 'USD',
  asOf: '2026-10-13',
};

const POSITION: DossierPositionSection = {
  provider: 'ibkr',
  assetClass: 'Equities',
  quantity: 10,
  currentValueUsd: 1000,
  costBasisUsd: 900,
  unrealizedPnlUsd: 100,
  unrealizedPnlPercent: 11.1,
  shareOfBookPercent: 12.5,
  taxLots: [],
};

function dossier(overrides: Partial<AssetDossierDto> = {}): AssetDossierDto {
  return {
    symbol: 'AAPL',
    position: null,
    thesis: null,
    valuation: null,
    analysts: null,
    recentNews: [],
    nextEarnings: null,
    radarSignals: [],
    generatedAt: '2026-10-13T00:00:00Z',
    ...overrides,
  };
}

function chart(overrides: Partial<AssetChartDto> = {}): AssetChartDto {
  return {
    symbol: 'AAPL',
    range: '1Y',
    currency: 'USD',
    points: [{date: '2026-10-13', close: 100, benchmark: 100}],
    benchmarkLabel: 'S&P 500',
    trades: [],
    averageCost: null,
    changePct: null,
    benchmarkChangePct: null,
    source: 'yahoo-chart',
    retrievedAt: '2026-10-13T01:00:00Z',
    ...overrides,
  };
}

function returnsOf(fillCount: number) {
  return {
    unrealizedUsd: 100,
    realizedUsd: 0,
    dividendsUsd: 0,
    currencyUsd: 0,
    totalUsd: 100,
    coverage: 'complete' as const,
    averageCostNative: 90,
    costCurrency: 'USD',
    fillCount,
  };
}

describe('DossierFactsUtils.signedPercent', () => {
  it.each([null, undefined, Number.NaN])('is null for %s', value => {
    expect(DossierFactsUtils.signedPercent(value)).toBeNull();
  });

  it('signs a rise', () => {
    expect(DossierFactsUtils.signedPercent(22.24)).toBe('+22.2%');
  });

  it('signs a fall', () => {
    expect(DossierFactsUtils.signedPercent(-1.96)).toBe('-2.0%');
  });

  it('leaves a value that rounds to zero unsigned', () => {
    expect(DossierFactsUtils.signedPercent(-0.01)).toBe('0.0%');
  });

  it('honours the digits', () => {
    expect(DossierFactsUtils.signedPercent(1.234, 2)).toBe('+1.23%');
  });
});

describe('DossierFactsUtils.tone', () => {
  it.each([null, undefined, 0, Number.POSITIVE_INFINITY])('has no tone for %s', value => {
    expect(DossierFactsUtils.tone(value)).toBeNull();
  });

  it('reads a rise as gain', () => {
    expect(DossierFactsUtils.tone(1)).toBe('gain');
  });

  it('reads a fall as loss', () => {
    expect(DossierFactsUtils.tone(-1)).toBe('loss');
  });
});

describe('DossierFactsUtils.performanceStrip', () => {
  it('is empty without performance', () => {
    expect(DossierFactsUtils.performanceStrip(null)).toEqual([]);
  });

  it('lists the year, the distance from the high and the S&P 500', () => {
    expect(DossierFactsUtils.performanceStrip(PERFORMANCE)).toEqual([
      {key: 'year', label: 'year', value: '+22.2%', tone: 'gain'},
      {key: 'from-high', label: 'from 52w high', value: '-2.0%', tone: 'loss'},
      {key: 'benchmark', label: 'S&P 500', value: '+14.1%', tone: 'gain'},
    ]);
  });

  it('leaves out a fact without a value', () => {
    const strip = DossierFactsUtils.performanceStrip({...PERFORMANCE, fromHighPct: null});

    expect(strip.map(i => i.key)).toEqual(['year', 'benchmark']);
  });
});

describe('DossierFactsUtils.thesisHook', () => {
  it('is null without a dossier', () => {
    expect(DossierFactsUtils.thesisHook(null)).toBeNull();
  });

  it('names the share of book for a holding', () => {
    expect(DossierFactsUtils.thesisHook(dossier({position: POSITION}))).toBe(
      'Held · 12.5% of book'
    );
  });

  it('says held when the share is unknown', () => {
    expect(
      DossierFactsUtils.thesisHook(dossier({position: {...POSITION, shareOfBookPercent: null}}))
    ).toBe('Held');
  });

  it('names the next earnings for a name not held', () => {
    const hook = DossierFactsUtils.thesisHook(
      dossier({
        nextEarnings: {
          ticker: 'AAPL',
          eventType: 'earnings',
          eventDate: '2026-10-30',
          isEstimate: false,
          source: 'test',
        },
      })
    );

    expect(hook).toMatch(/^Earnings .*30 Oct/);
  });

  it('names the latest radar signal otherwise', () => {
    const signal = {scanner: 'radar', severity: 'info', payload: {}};
    const hook = DossierFactsUtils.thesisHook(
      dossier({
        radarSignals: [
          {...signal, timestamp: '2026-10-01T00:00:00Z', signalType: 'GAP_UP'},
          {...signal, timestamp: '2026-10-05T00:00:00Z', signalType: 'VOLUME_SPIKE'},
        ],
      })
    );

    expect(hook).toBe('Signal · Volume spike');
  });

  it('is null with nothing to say', () => {
    expect(DossierFactsUtils.thesisHook(dossier())).toBeNull();
  });
});

describe('DossierFactsUtils.marketCap', () => {
  it.each([null, undefined, 0, Number.NaN])('is null for %s', value => {
    expect(DossierFactsUtils.marketCap(value, 'USD')).toBeNull();
  });

  it('abbreviates trillions', () => {
    expect(DossierFactsUtils.marketCap(3.12e12, 'USD')).toBe('$3.12T');
  });

  it('abbreviates billions', () => {
    expect(DossierFactsUtils.marketCap(812.4e9, 'EUR')).toBe('€812.40B');
  });

  it('shows a cap below a million in full', () => {
    expect(DossierFactsUtils.marketCap(950_000, 'USD')).toBe('$950,000');
  });
});

describe('DossierFactsUtils.fundamentals', () => {
  it('is empty without a valuation', () => {
    expect(DossierFactsUtils.fundamentals(null, PERFORMANCE)).toEqual([]);
  });

  it('is empty for crypto', () => {
    expect(DossierFactsUtils.fundamentals(valuation({notApplicable: true}), PERFORMANCE)).toEqual(
      []
    );
  });

  it('fills every cell it can and gives the rest a reason, never a zero', () => {
    const cells = DossierFactsUtils.fundamentals(valuation(), PERFORMANCE);
    const byKey = Object.fromEntries(cells.map(c => [c.key, c]));

    expect(cells).toHaveLength(10);
    expect(byKey['market-cap'].value).toBe('$3.12T');
    expect(byKey['forward-pe'].value).toBe('28.0x');
    expect(byKey['ev-ebitda']).toEqual(
      expect.objectContaining({value: null, reason: 'not reported', tone: null})
    );
    expect(byKey['dividend-yield'].value).toBe('0.51%');
    expect(byKey['year-change']).toEqual(
      expect.objectContaining({value: '+22.2%', tone: 'gain', reason: null})
    );
    expect(byKey['high-52w'].value).toBe('$102.00');
    expect(byKey['from-high'].tone).toBe('loss');
    expect(byKey['analyst-target'].value).toBe('$120.00 · +20.0%');
  });

  it('flags price facts without history and an absent target', () => {
    const cells = DossierFactsUtils.fundamentals(
      valuation({consensusTarget: null, currency: undefined}),
      undefined
    );
    const byKey = Object.fromEntries(cells.map(c => [c.key, c]));

    expect(byKey['high-52w'].reason).toBe('no price history');
    expect(byKey['analyst-target'].reason).toBe('no analyst target');
  });

  it('shows the target alone without an upside and rejects a negative multiple', () => {
    const cells = DossierFactsUtils.fundamentals(
      valuation({
        impliedUpsidePct: null,
        metrics: {...valuation().metrics, trailingPe: metric(-4)},
      }),
      PERFORMANCE
    );
    const byKey = Object.fromEntries(cells.map(c => [c.key, c]));

    expect(byKey['analyst-target'].value).toBe('$120.00');
    expect(byKey['trailing-pe'].reason).toBe('no positive earnings');
  });
});

describe('DossierFactsUtils.dataStatus', () => {
  it('is empty without a dossier', () => {
    expect(DossierFactsUtils.dataStatus(null, null)).toEqual([]);
  });

  it('lists the dossier and the ownership gap for a bare stock', () => {
    const rows = DossierFactsUtils.dataStatus(dossier(), null);

    expect(rows.map(r => r.key)).toEqual(['dossier', 'ownership']);
    expect(rows[1].note).toBe(OWNERSHIP_NOTE);
  });

  it('names every section it read, its source and when', () => {
    const rows = DossierFactsUtils.dataStatus(
      dossier({
        valuation: valuation({isStale: true}),
        analysts: {
          coverage: 'covered',
          recentActions: [],
          trends: [
            {
              period: '2026-09-01',
              strongBuy: 1,
              buy: 1,
              hold: 1,
              sell: 0,
              strongSell: 0,
              source: 'finnhub',
              ingestedAt: '2026-10-01T00:00:00Z',
            },
            {
              period: '2026-10-01',
              strongBuy: 1,
              buy: 1,
              hold: 1,
              sell: 0,
              strongSell: 0,
              source: 'yahoo',
              ingestedAt: '2026-10-11T00:00:00Z',
            },
          ],
        },
        recentNews: [
          {
            id: 'n-1',
            source: 'finnhub',
            title: 'Old',
            url: 'https://example.com/1',
            summary: null,
            tickers: [],
            categories: [],
            publishedAt: '2026-10-01T00:00:00Z',
          },
          {
            id: 'n-2',
            source: 'finnhub',
            title: 'New',
            url: 'https://example.com/2',
            summary: null,
            tickers: [],
            categories: [],
            publishedAt: '2026-10-12T00:00:00Z',
          },
        ],
        position: {...POSITION, returns: returnsOf(1)},
      }),
      chart()
    );
    const byKey = Object.fromEntries(rows.map(r => [r.key, r]));

    expect(rows.map(r => r.key)).toEqual([
      'dossier',
      'valuation',
      'chart',
      'analysts',
      'news',
      'returns',
      'ownership',
    ]);
    expect(byKey['valuation']).toEqual(
      expect.objectContaining({
        source: 'Yahoo Finance, SEC EDGAR',
        at: '2026-10-13T00:00:00Z',
        note: 'stale',
      })
    );
    expect(byKey['chart'].source).toBe('Yahoo Finance');
    expect(byKey['analysts']).toEqual(
      expect.objectContaining({source: 'Yahoo Finance', at: '2026-10-11T00:00:00Z'})
    );
    expect(byKey['news'].at).toBe('2026-10-12T00:00:00Z');
    expect(byKey['returns'].note).toBe('1 fill · complete');
  });

  it('falls back to the retrieval time, pluralises fills and hides ownership for crypto', () => {
    const stock = DossierFactsUtils.dataStatus(
      dossier({
        valuation: valuation({priceAsOf: null}),
        position: {...POSITION, returns: returnsOf(3)},
      }),
      chart({points: []})
    );
    expect(stock.find(r => r.key === 'valuation')?.at).toBe('2026-10-12T00:00:00Z');
    expect(stock.find(r => r.key === 'returns')?.note).toBe('3 fills · complete');
    expect(stock.some(r => r.key === 'chart')).toBe(false);

    const crypto = DossierFactsUtils.dataStatus(
      dossier({valuation: valuation({notApplicable: true})}),
      null
    );
    expect(crypto.map(r => r.key)).toEqual(['dossier']);
  });
});

describe('DossierFactsUtils.distinct', () => {
  it('is a dash for no names', () => {
    expect(DossierFactsUtils.distinct([])).toBe('-');
  });

  it('keeps first-seen order without repeats', () => {
    expect(DossierFactsUtils.distinct(['B', 'A', 'B'])).toBe('B, A');
  });
});
