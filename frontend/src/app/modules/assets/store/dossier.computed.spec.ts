import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it} from 'vitest';

import {
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
  severity: 'high',
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
});
