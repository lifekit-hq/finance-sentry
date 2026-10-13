import {
  type AssetDossierDto,
  type RecommendationTrendDto,
  type ThesisDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {DossierViewUtils} from './dossier-view.utils';

function trend(overrides: Partial<RecommendationTrendDto> = {}): RecommendationTrendDto {
  return {
    period: '2026-08-01',
    strongBuy: 0,
    buy: 0,
    hold: 0,
    sell: 0,
    strongSell: 0,
    source: 'yahoo_finance',
    ingestedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

function valuation(overrides: Partial<ValuationSnapshotDto> = {}): ValuationSnapshotDto {
  return {
    ticker: 'GRAB',
    notApplicable: false,
    price: 3.5,
    isStale: false,
    metrics: {
      trailingPe: {
        value: null,
        fiveYearAvg: null,
        historyWindowYears: null,
        historyUnavailable: true,
      },
      forwardPe: {
        value: null,
        fiveYearAvg: null,
        historyWindowYears: null,
        historyUnavailable: true,
      },
      evToEbitda: {
        value: null,
        fiveYearAvg: null,
        historyWindowYears: null,
        historyUnavailable: true,
      },
      dividendYield: {
        value: null,
        fiveYearAvg: null,
        historyWindowYears: null,
        historyUnavailable: true,
      },
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
    ...overrides,
  };
}

const THESIS: ThesisDto = {
  id: 't-1',
  ticker: 'GRAB',
  thesisText: 'A thesis.',
  keyDataPoints: [],
  catalysts: [],
  invalidationTriggers: [],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  brokenAt: null,
  brokenReason: null,
  entryPrice: null,
};

function dossier(overrides: Partial<AssetDossierDto> = {}): AssetDossierDto {
  return {
    symbol: 'GRAB',
    position: null,
    thesis: null,
    valuation: null,
    analysts: null,
    recentNews: [],
    nextEarnings: null,
    radarSignals: [],
    generatedAt: '2026-09-01T10:30:00Z',
    ...overrides,
  };
}

describe('DossierViewUtils.recommendationSummary', () => {
  it('is null without any month', () => {
    expect(DossierViewUtils.recommendationSummary([])).toBeNull();
  });

  it('is null when nobody covered the name', () => {
    expect(DossierViewUtils.recommendationSummary([trend()])).toBeNull();
  });

  it('reads the newest month as "N of M" for the winning side', () => {
    const summary = DossierViewUtils.recommendationSummary([
      trend({period: '2026-07-01', hold: 20}),
      trend({period: '2026-08-01', strongBuy: 9, buy: 12, hold: 4, sell: 1}),
    ]);

    expect(summary).toMatchObject({
      period: '2026-08-01',
      total: 26,
      verdict: 'Buy',
      verdictCount: 21,
    });
  });

  it('reads a hold-heavy month as Hold', () => {
    const summary = DossierViewUtils.recommendationSummary([trend({buy: 2, hold: 10, sell: 1})]);

    expect(summary).toMatchObject({verdict: 'Hold', verdictCount: 10});
  });

  it('reads a bearish month as Sell, summing sell and strong sell', () => {
    const summary = DossierViewUtils.recommendationSummary([
      trend({buy: 1, hold: 2, sell: 3, strongSell: 4}),
    ]);

    expect(summary).toMatchObject({verdict: 'Sell', verdictCount: 7});
  });

  it('lets the bullish side win a tie', () => {
    const summary = DossierViewUtils.recommendationSummary([trend({buy: 5, hold: 5})]);

    expect(summary?.verdict).toBe('Buy');
  });

  it('slices the bar by share and drops empty buckets', () => {
    const summary = DossierViewUtils.recommendationSummary([
      trend({strongBuy: 1, buy: 1, hold: 2}),
    ]);

    expect(summary?.segments.map(s => [s.bucket, s.count, s.sharePct])).toEqual([
      ['strongBuy', 1, 25],
      ['buy', 1, 25],
      ['hold', 2, 50],
    ]);
    expect(summary?.segments[0].barClass).toBe('bg-status-success');
  });
});

describe('DossierViewUtils.heroProfile', () => {
  it('names the company with its sector and industry', () => {
    const profile = DossierViewUtils.heroProfile(
      dossier({
        valuation: valuation({
          name: 'Grab Holdings Limited',
          sector: 'Technology',
          industry: 'Software - Application',
        }),
      })
    );

    expect(profile).toEqual({
      name: 'Grab Holdings Limited',
      detail: 'Technology · Software - Application',
    });
  });

  it('keeps a lone sector without a dangling separator', () => {
    const profile = DossierViewUtils.heroProfile(
      dossier({valuation: valuation({sector: 'Technology'})})
    );

    expect(profile.detail).toBe('Technology');
  });

  it('falls back to the asset class of the holding', () => {
    const profile = DossierViewUtils.heroProfile(
      dossier({
        position: {
          provider: 'binance',
          assetClass: 'Crypto',
          quantity: 1,
          currentValueUsd: 100,
          costBasisUsd: null,
          unrealizedPnlUsd: null,
          unrealizedPnlPercent: null,
          shareOfBookPercent: null,
          taxLots: [],
        },
      })
    );

    expect(profile).toEqual({name: null, detail: 'Crypto'});
  });

  it('ignores a not-applicable valuation', () => {
    const profile = DossierViewUtils.heroProfile(
      dossier({valuation: valuation({notApplicable: true, name: 'Ignored', sector: 'Ignored'})})
    );

    expect(profile).toEqual({name: null, detail: null});
  });
});

describe('DossierViewUtils.isCrypto', () => {
  const holding = (assetClass: string): AssetDossierDto['position'] => ({
    provider: 'binance',
    assetClass,
    quantity: 1,
    currentValueUsd: 100,
    costBasisUsd: null,
    unrealizedPnlUsd: null,
    unrealizedPnlPercent: null,
    shareOfBookPercent: null,
    taxLots: [],
  });

  it('is true for a crypto holding even when the valuation source failed', () => {
    expect(DossierViewUtils.isCrypto(dossier({position: holding('Crypto'), valuation: null}))).toBe(
      true
    );
  });

  it('is true for an unheld symbol the valuation source marks not applicable', () => {
    expect(DossierViewUtils.isCrypto(dossier({valuation: valuation({notApplicable: true})}))).toBe(
      true
    );
  });

  it('is false for an equity and without a dossier', () => {
    expect(DossierViewUtils.isCrypto(dossier({position: holding('Equities')}))).toBe(false);
    expect(DossierViewUtils.isCrypto(null)).toBe(false);
  });
});

describe('DossierViewUtils.dividendYield', () => {
  it('formats the fraction as a percent', () => {
    expect(DossierViewUtils.dividendYield(0.0295)).toBe('2.95%');
  });

  it('is null for a missing or non-finite yield', () => {
    expect(DossierViewUtils.dividendYield(null)).toBeNull();
    expect(DossierViewUtils.dividendYield(Number.NaN)).toBeNull();
  });
});

describe('DossierViewUtils.sectionChips', () => {
  it('is empty without a dossier', () => {
    expect(DossierViewUtils.sectionChips(null)).toEqual([]);
  });

  it('has no chip for a section that does not exist, only the data status', () => {
    expect(DossierViewUtils.sectionChips(dossier()).map(c => c.label)).toEqual(['Data']);
  });

  it('skips the valuation of a not-applicable symbol', () => {
    const chips = DossierViewUtils.sectionChips(
      dossier({valuation: valuation({notApplicable: true})})
    );

    expect(chips.map(c => c.label)).toEqual(['Data']);
  });

  const PRICED = {
    lastClose: 1,
    yearChangePct: null,
    high52Week: null,
    low52Week: null,
    fromHighPct: null,
    benchmarkYearChangePct: null,
    currency: 'USD',
    asOf: null,
  };

  it('keeps "Valuation" for a priced asset without a valuation (it carries the user target)', () => {
    const chips = DossierViewUtils.sectionChips(dossier({valuation: null, performance: PRICED}));

    expect(chips.map(c => c.label)).toEqual(['Chart', 'Valuation', 'Data']);
  });

  it('labels the price card "Price & your target" for crypto and drops its analysts and valuation chips', () => {
    const chips = DossierViewUtils.sectionChips(
      dossier({
        valuation: valuation({notApplicable: true}),
        analysts: {recentActions: [], trends: [], coverage: 'inUniverse'},
        performance: PRICED,
      })
    );

    expect(chips.map(c => c.label)).toEqual(['Chart', 'Price & your target', 'Data']);
  });

  it('keeps a crypto holding off the analysts chip even when its valuation looks applicable', () => {
    const chips = DossierViewUtils.sectionChips(
      dossier({
        position: {
          provider: 'binance',
          assetClass: 'Crypto',
          quantity: 1,
          currentValueUsd: 100,
          costBasisUsd: null,
          unrealizedPnlUsd: null,
          unrealizedPnlPercent: null,
          shareOfBookPercent: null,
          taxLots: [],
        },
        valuation: valuation(),
        analysts: {recentActions: [], trends: [], coverage: 'inUniverse'},
      })
    );

    expect(chips.map(c => c.label)).toEqual(['Data']);
  });

  it('lists the sections that exist in page order', () => {
    const chips = DossierViewUtils.sectionChips(
      dossier({
        thesis: THESIS,
        valuation: valuation(),
        checks: {
          axes: [{key: 'fit', label: 'Fit', passed: 0, evaluated: 0, rules: []}],
        },
        position: {
          provider: 'ibkr',
          assetClass: 'Equities',
          quantity: 1,
          currentValueUsd: 1,
          costBasisUsd: null,
          unrealizedPnlUsd: null,
          unrealizedPnlPercent: null,
          shareOfBookPercent: null,
          taxLots: [],
          returns: {
            unrealizedUsd: 0,
            realizedUsd: null,
            dividendsUsd: null,
            currencyUsd: null,
            totalUsd: null,
            coverage: 'unavailable',
            averageCostNative: null,
            costCurrency: null,
            fillCount: 0,
          },
        },
        analysts: {recentActions: [], trends: [], coverage: 'inUniverse'},
        nextEarnings: {
          ticker: 'GRAB',
          eventType: 'Earnings',
          eventDate: '2026-11-10',
          isEstimate: false,
          source: 'yahoo',
        },
        recentNews: [
          {
            id: '1',
            source: 'yahoo:GRAB',
            title: 't',
            url: 'https://x',
            summary: null,
            tickers: [],
            categories: [],
            publishedAt: '2026-09-01T00:00:00Z',
          },
        ],
        radarSignals: [
          {
            timestamp: '2026-09-01T00:00:00Z',
            scanner: 'radar',
            signalType: 'X',
            severity: 'info',
            payload: {},
          },
        ],
      })
    );

    expect(chips.map(c => c.label)).toEqual([
      'Checks',
      'Returns',
      'Valuation',
      'Analysts',
      'Plan',
      'Earnings',
      'News',
      'Signals',
      'Data',
    ]);
  });
});
