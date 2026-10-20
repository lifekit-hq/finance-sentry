import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierAnalystsSection,
  type DossierPositionSection,
  type DossierQuoteDto,
  type DossierSignalItem,
  type EarningsEventDto,
  type NewsArticleDto,
  type ThesisDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {dossierComputed} from './dossier.computed';
import {type DossierState} from './dossier.state';

function buildSignals(overrides: Partial<DossierState> = {}) {
  return {
    dossier: signal<Nullable<AssetDossierDto>>(overrides.dossier ?? null),
    ledgerRead: signal<Nullable<AssetLedgerReadDto>>(overrides.ledgerRead ?? null),
    isThesisExpanded: signal<boolean>(overrides.isThesisExpanded ?? false),
    quote: signal<Nullable<DossierQuoteDto>>(overrides.quote ?? null),
    chart: signal<Nullable<AssetChartDto>>(overrides.chart ?? null),
    chartStatus: signal<AsyncStatus>('idle'),
    chartErrorMessage: signal<string>(''),
    targetDraft: signal<string>(overrides.targetDraft ?? ''),
  };
}

function ledgerRead(overrides: Partial<AssetLedgerReadDto> = {}): AssetLedgerReadDto {
  return {
    symbol: 'AAPL',
    narrative: 'A read.',
    generatedAt: '2026-09-03T00:00:00Z',
    isStale: false,
    cached: true,
    ...overrides,
  };
}

function emptyDossier(overrides: Partial<AssetDossierDto> = {}): AssetDossierDto {
  return {
    symbol: 'ZZZZ',
    position: null,
    thesis: null,
    valuation: null,
    analysts: null,
    recentNews: [],
    nextEarnings: null,
    radarSignals: [],
    generatedAt: '2026-09-03T00:00:00Z',
    ...overrides,
  };
}

const POSITION: DossierPositionSection = {
  provider: 'ibkr',
  assetClass: 'Equities',
  quantity: 1,
  currentValueUsd: 100,
  costBasisUsd: null,
  unrealizedPnlUsd: null,
  unrealizedPnlPercent: null,
  shareOfBookPercent: null,
  taxLots: [],
};

const THESIS: ThesisDto = {
  id: 't-1',
  ticker: 'ZZZZ',
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

const NO_METRIC = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

function valuation(notApplicable: boolean): ValuationSnapshotDto {
  return {
    ticker: 'ZZZZ',
    notApplicable,
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
    retrievedAt: '2026-09-03T00:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: null,
  };
}

const ANALYSTS: DossierAnalystsSection = {recentActions: [], trends: [], coverage: 'covered'};

const EARNINGS: EarningsEventDto = {
  ticker: 'ZZZZ',
  eventType: 'earnings',
  eventDate: '2026-10-01',
  isEstimate: false,
  source: 'test',
};

const NEWS: NewsArticleDto = {
  id: 'n-1',
  source: 'test',
  title: 'Headline',
  url: 'https://example.com',
  summary: null,
  tickers: ['ZZZZ'],
  categories: [],
  publishedAt: '2026-09-01T00:00:00Z',
};

const SIGNAL: DossierSignalItem = {
  timestamp: '2026-09-01T00:00:00Z',
  scanner: 'radar',
  signalType: 'VOLUME_SPIKE',
  severity: 'alerted',
  payload: {},
};

describe('dossierComputed', () => {
  it('hasDossierSections is false before a dossier has loaded', () => {
    const store = buildSignals();
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).hasDossierSections()).toBe(false);
    });
  });

  it('hasDossierSections is false when every section is null or empty', () => {
    const store = buildSignals({dossier: emptyDossier()});
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).hasDossierSections()).toBe(false);
    });
  });

  it('hasDossierSections ignores a not-applicable valuation (crypto)', () => {
    const store = buildSignals({dossier: emptyDossier({valuation: valuation(true)})});
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).hasDossierSections()).toBe(false);
    });
  });

  it.each<[string, Partial<AssetDossierDto>]>([
    ['position', {position: POSITION}],
    ['thesis', {thesis: THESIS}],
    ['valuation', {valuation: valuation(false)}],
    ['analysts', {analysts: ANALYSTS}],
    ['nextEarnings', {nextEarnings: EARNINGS}],
    ['recentNews', {recentNews: [NEWS]}],
    ['radarSignals', {radarSignals: [SIGNAL]}],
  ])('hasDossierSections is true when only %s has data', (_name, overrides) => {
    const store = buildSignals({dossier: emptyDossier(overrides)});
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).hasDossierSections()).toBe(true);
    });
  });

  it('heroProfile is null before a dossier has loaded', () => {
    const store = buildSignals();
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).heroProfile()).toBeNull();
    });
  });

  it('position and checks views are empty before a dossier has loaded', () => {
    const store = buildSignals();
    TestBed.runInInjectionContext(() => {
      const computedSignals = dossierComputed(store);
      expect(computedSignals.sizeCap()).toBeNull();
      expect(computedSignals.wheel()).toBeNull();
      expect(computedSignals.checkAxes()).toEqual([]);
    });
  });

  it('heroProfile names the company from the valuation', () => {
    const store = buildSignals({
      dossier: emptyDossier({
        valuation: {...valuation(false), name: 'Grab Holdings Limited', sector: 'Technology'},
      }),
    });
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).heroProfile()).toEqual({
        name: 'Grab Holdings Limited',
        detail: 'Technology',
      });
    });
  });

  it('sectionChips lists only the sections that exist', () => {
    const store = buildSignals({dossier: emptyDossier({thesis: THESIS, radarSignals: [SIGNAL]})});
    TestBed.runInInjectionContext(() => {
      expect(
        dossierComputed(store)
          .sectionChips()
          .map(c => c.label)
      ).toEqual(['Plan', 'Signals', 'Data']);
    });
  });

  it('recommendationSummary is null without a trend', () => {
    const store = buildSignals({dossier: emptyDossier({analysts: ANALYSTS})});
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).recommendationSummary()).toBeNull();
    });
  });

  it('recommendationSummary reads the latest month as a verdict', () => {
    const store = buildSignals({
      dossier: emptyDossier({
        analysts: {
          ...ANALYSTS,
          trends: [
            {
              period: '2026-08-01',
              strongBuy: 9,
              buy: 12,
              hold: 4,
              sell: 1,
              strongSell: 0,
              source: 'yahoo_finance',
              ingestedAt: '2026-09-01T00:00:00Z',
            },
          ],
        },
      }),
    });
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).recommendationSummary()).toMatchObject({
        total: 26,
        verdict: 'Buy',
        verdictCount: 21,
      });
    });
  });

  it('ledgerReadNarrative is empty when nothing has been generated', () => {
    const store = buildSignals({ledgerRead: ledgerRead({narrative: null})});
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).ledgerReadNarrative()).toBe('');
      expect(dossierComputed(store).isLedgerReadStale()).toBe(false);
    });
  });

  it('an absent cached read is not flagged stale even though the API says so', () => {
    const store = buildSignals({
      ledgerRead: ledgerRead({narrative: null, generatedAt: null, isStale: true, cached: false}),
    });
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).isLedgerReadStale()).toBe(false);
    });
  });

  it('ledgerReadNarrative and stale flag surface the cached read', () => {
    const store = buildSignals({
      ledgerRead: ledgerRead({narrative: 'AAPL is fine.', isStale: true}),
    });
    TestBed.runInInjectionContext(() => {
      expect(dossierComputed(store).ledgerReadNarrative()).toBe('AAPL is fine.');
      expect(dossierComputed(store).isLedgerReadStale()).toBe(true);
    });
  });

  describe('thesis paragraphs', () => {
    const twoParagraphs = emptyDossier({
      thesis: {...THESIS, thesisText: 'First point.\n\nSecond point.\n\n  \n\nThird.'},
    });

    it('is empty without a thesis', () => {
      TestBed.runInInjectionContext(() => {
        const computeds = dossierComputed(buildSignals({dossier: emptyDossier()}));
        expect(computeds.thesisParagraphs()).toEqual([]);
        expect(computeds.thesisHasMore()).toBe(false);
      });
    });

    it('shows only the first paragraph until expanded', () => {
      TestBed.runInInjectionContext(() => {
        const computeds = dossierComputed(buildSignals({dossier: twoParagraphs}));
        expect(computeds.thesisHasMore()).toBe(true);
        expect(computeds.visibleThesisParagraphs()).toEqual(['First point.']);
      });
    });

    it('shows every non-blank paragraph once expanded', () => {
      TestBed.runInInjectionContext(() => {
        const computeds = dossierComputed(
          buildSignals({dossier: twoParagraphs, isThesisExpanded: true})
        );
        expect(computeds.visibleThesisParagraphs()).toEqual([
          'First point.',
          'Second point.',
          'Third.',
        ]);
      });
    });

    it('has no "read more" for a single paragraph', () => {
      TestBed.runInInjectionContext(() => {
        const computeds = dossierComputed(
          buildSignals({dossier: emptyDossier({thesis: {...THESIS, thesisText: 'Only one.'}})})
        );
        expect(computeds.thesisHasMore()).toBe(false);
      });
    });
  });

  describe('quote header', () => {
    const QUOTE: DossierQuoteDto = {
      ticker: 'AAPL',
      price: 189.3,
      previousClose: 187,
      changePct: 1.23,
      currency: 'USD',
    };

    it('formats the stored quote for the header', () => {
      const store = buildSignals({quote: QUOTE});
      TestBed.runInInjectionContext(() => {
        expect(dossierComputed(store).quoteHeader()).toEqual({
          priceText: '$189.30',
          changeText: '+1.23%',
          direction: 'up',
        });
      });
    });

    it('is null when the quote is missing or failed', () => {
      const store = buildSignals({quote: null});
      TestBed.runInInjectionContext(() => {
        expect(dossierComputed(store).quoteHeader()).toBeNull();
      });
    });
  });

  describe('stage 2', () => {
    const CHECKS: AssetDossierDto['checks'] = {
      axes: [
        {
          key: 'value',
          label: 'Value',
          passed: 1,
          evaluated: 2,
          rules: [
            {
              key: 'a',
              label: 'A',
              passed: true,
              value: 1,
              threshold: 2,
              unit: 'multiple',
              note: null,
            },
            {
              key: 'b',
              label: 'B',
              passed: false,
              value: null,
              threshold: null,
              unit: 'none',
              note: null,
            },
          ],
        },
        {key: 'momentum', label: 'Momentum', passed: 1, evaluated: 1, rules: []},
      ],
    };
    const PERFORMANCE: NonNullable<AssetDossierDto['performance']> = {
      lastClose: 90,
      yearChangePct: 5,
      high52Week: 100,
      low52Week: 80,
      fromHighPct: -10,
      benchmarkYearChangePct: 3,
      currency: 'USD',
      asOf: '2026-10-13',
    };

    function withChart(status: AsyncStatus, error = '') {
      return {
        ...buildSignals(),
        chartStatus: signal<AsyncStatus>(status),
        chartErrorMessage: signal<string>(error),
      };
    }

    it('reads crypto from a not-applicable valuation', () => {
      const store = buildSignals({dossier: emptyDossier({valuation: valuation(true)})});
      TestBed.runInInjectionContext(() => {
        expect(dossierComputed(store).isCrypto()).toBe(true);
      });
    });

    it('counts a dossier with only price history as having sections', () => {
      const store = buildSignals({dossier: emptyDossier({performance: PERFORMANCE})});
      TestBed.runInInjectionContext(() => {
        expect(dossierComputed(store).hasDossierSections()).toBe(true);
      });
    });

    it('totals the checks and hands each question-led section its axis', () => {
      const store = buildSignals({dossier: emptyDossier({checks: CHECKS})});
      TestBed.runInInjectionContext(() => {
        const computed = dossierComputed(store);
        expect(computed.checksPassed()).toBe(2);
        // Counts named rules, including the one the data could not decide.
        expect(computed.checksTotal()).toBe(2);
        expect(computed.sectionChecks()['valuation']?.key).toBe('value');
        expect(computed.sectionChecks()['chart']?.key).toBe('momentum');
        expect(computed.sectionChecks()['analysts']).toBeNull();
        expect(computed.wheel()).toBeNull();
      });
    });

    it.each<[AsyncStatus, string, string]>([
      ['error', 'Failed', 'error'],
      ['error', '', 'success'],
      ['loading', '', 'loading'],
      ['idle', '', 'loading'],
      ['success', '', 'success'],
    ])('maps chart status %s (error "%s") to %s', (status, error, expected) => {
      const store = withChart(status, error);
      TestBed.runInInjectionContext(() => {
        expect(dossierComputed(store).chartAsyncStatus()).toBe(expected);
      });
    });

    it('prices "Is it cheap?" from the quote, then the valuation, then the last close', () => {
      const quote: DossierQuoteDto = {
        ticker: 'ZZZZ',
        price: 95,
        previousClose: 94,
        changePct: 1,
        currency: 'USD',
      };
      const priced = {...valuation(false), price: 92};
      TestBed.runInInjectionContext(() => {
        expect(
          dossierComputed(
            buildSignals({
              dossier: emptyDossier({valuation: priced, performance: PERFORMANCE}),
              quote,
            })
          ).currentPrice()
        ).toBe(95);
        expect(
          dossierComputed(
            buildSignals({dossier: emptyDossier({valuation: priced, performance: PERFORMANCE})})
          ).currentPrice()
        ).toBe(92);
        expect(
          dossierComputed(
            buildSignals({dossier: emptyDossier({performance: PERFORMANCE})})
          ).currentPrice()
        ).toBe(90);
        expect(dossierComputed(buildSignals()).currentPrice()).toBeNull();
      });
    });

    it('builds the target band, the parsed draft, the fundamentals and the data status', () => {
      const store = buildSignals({
        dossier: emptyDossier({
          valuation: {...valuation(false), consensusTarget: 120},
          performance: PERFORMANCE,
          userTarget: {targetPrice: 110, currency: 'USD', updatedAt: '2026-10-13T00:00:00Z'},
          position: {...POSITION, shareOfBookPercent: 5, positionCapPercent: 10},
        }),
        targetDraft: '125.5',
      });
      TestBed.runInInjectionContext(() => {
        const computed = dossierComputed(store);
        expect(computed.targetBand()?.markers.map(m => m.key)).toEqual([
          'price',
          'analyst',
          'mine',
        ]);
        expect(computed.parsedTargetDraft()).toBe(125.5);
        expect(computed.fundamentals()).toHaveLength(10);
        expect(computed.dataStatus().map(r => r.key)).toContain('valuation');
        expect(computed.performanceStrip()).toHaveLength(3);
        expect(computed.thesisHook()).toBe('Held · 5.0% of book');
        expect(computed.sizeCap()?.overCap).toBe(false);
        expect(computed.returnsView()).toBeNull();
        expect(computed.chartView()).toBeNull();
      });
    });

    it('has no band, fundamentals or data status before a dossier has loaded', () => {
      TestBed.runInInjectionContext(() => {
        const computed = dossierComputed(buildSignals());
        expect(computed.targetBand()).toBeNull();
        expect(computed.fundamentals()).toEqual([]);
        expect(computed.dataStatus()).toEqual([]);
        expect(computed.checkAxes()).toEqual([]);
        expect(computed.isCrypto()).toBe(false);
      });
    });
  });
});
