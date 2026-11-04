import {computed, signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {vi} from 'vitest';

import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type ChartRangeId,
  type DossierAnalystsSection,
  type DossierPositionSection,
  type DossierQuoteDto,
  type DossierQuoteHeader,
  type MetricValue,
  type ThesisDto,
  type ValuationSnapshotDto,
} from '../../models/dossier/dossier.model';
import {dossierComputed} from '../../store/dossier.computed';
import {DossierViewUtils} from '../../utils/dossier-view.utils';

const THESIS: ThesisDto = {
  id: 't1',
  ticker: 'DRAM',
  thesisText: 'Memory cycle is turning.',
  keyDataPoints: [],
  catalysts: [],
  invalidationTriggers: [
    {
      metric: 'revenue_yoy',
      direction: 'lessThan',
      threshold: 0.3,
      proxyTicker: null,
      consecutivePeriods: 2,
      periodType: 'quarterly',
    },
  ],
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  brokenAt: null,
  brokenReason: null,
  entryPrice: null,
};

export const POSITION: DossierPositionSection = {
  provider: 'ibkr',
  assetClass: 'Equities',
  quantity: 10,
  currentValueUsd: 431.94,
  costBasisUsd: 400,
  unrealizedPnlUsd: 31.94,
  unrealizedPnlPercent: 7.985,
  shareOfBookPercent: null,
  taxLots: [
    {
      quantity: 10,
      currentValueUsd: 431.94,
      averageCostUsd: 40,
      costBasisUsd: 400,
      unrealizedPnlUsd: 31.94,
      unrealizedPnlPercent: 7.985,
      acquiredAt: '2025-01-01T00:00:00Z',
      isLongTerm: true,
    },
  ],
};

export const DOSSIER_ANALYSTS: DossierAnalystsSection = {
  recentActions: [
    {
      ticker: 'DRAM',
      firm: 'Needham',
      actionType: 'upgrade',
      priorRating: null,
      newRating: 'Buy',
      priorTarget: null,
      newTarget: 500,
      actionDate: '2026-09-01',
      source: 'finnhub',
      sourceUrl: 'https://example.com/needham',
      ingestedAt: '2026-09-01T00:00:00Z',
    },
    {
      ticker: 'DRAM',
      firm: 'Barclays',
      actionType: 'reiterate',
      priorRating: null,
      newRating: null,
      priorTarget: null,
      newTarget: null,
      actionDate: '2026-09-02',
      source: 'finnhub',
      sourceUrl: null,
      ingestedAt: '2026-09-02T00:00:00Z',
    },
  ],
  coverage: 'inUniverse',
  trends: [
    {
      period: '2026-08-01',
      strongBuy: 5,
      buy: 12,
      hold: 8,
      sell: 1,
      strongSell: 0,
      source: 'x',
      ingestedAt: '2026-09-01T00:00:00Z',
    },
  ],
};

const NO_METRIC: MetricValue = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

export const STALE_VALUATION: ValuationSnapshotDto = {
  ticker: 'DRAM',
  notApplicable: false,
  price: 43,
  isStale: true,
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
};

const NEWS_AGE_DAYS = 3;
const HOURS_PER_DAY = 24;
const MINUTES_PER_HOUR = 60;
const SECONDS_PER_MINUTE = 60;
const MS_PER_SECOND = 1000;
const NEWS_AGE_MS =
  NEWS_AGE_DAYS * HOURS_PER_DAY * MINUTES_PER_HOUR * SECONDS_PER_MINUTE * MS_PER_SECOND;

export const DOSSIER: AssetDossierDto = {
  symbol: 'DRAM',
  position: POSITION,
  thesis: THESIS,
  valuation: null,
  analysts: DOSSIER_ANALYSTS,
  recentNews: [
    {
      id: 'n1',
      source: 'Wire',
      title: 'Memory prices climb',
      url: 'https://example.com/a',
      summary: 'A long summary that should not render.',
      tickers: ['DRAM'],
      categories: [],
      publishedAt: new Date(Date.now() - NEWS_AGE_MS).toISOString(),
    },
  ],
  nextEarnings: null,
  radarSignals: [],
  generatedAt: new Date().toISOString(),
};

export function mkRecommendation(overrides: Partial<RecommendationDto> = {}): RecommendationDto {
  return {
    id: 'rec-1',
    ticker: 'DRAM',
    action: 'Trim',
    status: 'Pending',
    createdBy: 'Ledger',
    origin: 'Ledger',
    triggerKind: 'RiskRule',
    triggerRef: 'concentration',
    rationale: 'Above the single-name cap.',
    confidence: 'Medium',
    invalidationTrigger: 'Back under the cap.',
    evidence: [],
    sizeUsd: 2080,
    sizeShares: 660,
    shareOfBookBeforePct: 12.2,
    shareOfBookAfterPct: 9.5,
    limitPrice: 3.13,
    limitCurrency: 'USD',
    venue: 'IBKR',
    orderText: 'SELL 660 DRAM / LIMIT 3.13 USD DAY / IBKR',
    createdAt: '2026-10-12T09:00:00Z',
    expiresAt: '2026-10-19T09:00:00Z',
    decidedAt: null,
    decisionNote: null,
    execution: 'Pending',
    executedAt: null,
    subjectReturnPct: null,
    benchmarkReturnPct: null,
    excessReturnPct: null,
    decisionValuePct: null,
    outcomeScoredAt: null,
    ...overrides,
  };
}

// eslint-disable-next-line @typescript-eslint/explicit-module-boundary-types -- the mock store's shape is inferred from the real computeds it wraps; MockDossierStore derives from it
function createMockStore() {
  const dossier = signal<AssetDossierDto | null>(DOSSIER);
  const chart = signal<AssetChartDto | null>(null);
  const chartStatus = signal<AsyncStatus>('success');
  const chartErrorMessage = signal('');
  const targetDraft = signal('');
  const quote = signal<DossierQuoteDto | null>(null);
  const derived = dossierComputed({
    dossier,
    ledgerRead: signal(null),
    isThesisExpanded: signal(false),
    quote,
    chart,
    chartStatus,
    chartErrorMessage,
    targetDraft,
  });
  return {
    isDossierLoading: signal(false),
    dossierErrorMessage: signal(''),
    dossier,
    hasDossierSections: signal(true),
    heroProfile: computed(() => {
      const current = dossier();
      return current ? DossierViewUtils.heroProfile(current) : null;
    }),
    sectionChips: computed(() => DossierViewUtils.sectionChips(dossier())),
    recommendationSummary: computed(() =>
      DossierViewUtils.recommendationSummary(dossier()?.analysts?.trends ?? [])
    ),
    ledgerRead: signal<{generatedAt: string | null} | null>(null),
    isLedgerReadLoading: signal(false),
    isLedgerReadStale: signal(false),
    ledgerReadNarrative: signal(''),
    ledgerReadErrorMessage: signal(''),
    visibleThesisParagraphs: signal(['Memory cycle is turning.']),
    thesisHasMore: signal(false),
    isThesisExpanded: signal(false),
    quoteHeader: signal<DossierQuoteHeader | null>(null),
    isQuoteLoading: signal(false),
    chart,
    chartStatus,
    chartErrorMessage,
    chartRange: signal<ChartRangeId>('1Y'),
    targetDraft,
    isEditingTarget: signal(false),
    isTargetLoading: signal(false),
    targetErrorMessage: signal(''),
    isCrypto: derived.isCrypto,
    usableValuation: derived.usableValuation,
    hasValuationCard: derived.hasValuationCard,
    valuationCardTitle: derived.valuationCardTitle,
    hasAnalysts: derived.hasAnalysts,
    performanceStrip: derived.performanceStrip,
    thesisHook: derived.thesisHook,
    sizeCap: derived.sizeCap,
    positionUnitPrices: derived.positionUnitPrices,
    returnsView: derived.returnsView,
    wheel: derived.wheel,
    checkAxes: derived.checkAxes,
    checksPassed: derived.checksPassed,
    checksTotal: derived.checksTotal,
    sectionChecks: derived.sectionChecks,
    chartView: derived.chartView,
    chartAsyncStatus: derived.chartAsyncStatus,
    targetBand: derived.targetBand,
    parsedTargetDraft: derived.parsedTargetDraft,
    fundamentals: derived.fundamentals,
    dataStatus: derived.dataStatus,
    toggleThesisExpanded: vi.fn(),
    generateLedgerRead: vi.fn(),
    loadChart: vi.fn(),
    selectChartRange: vi.fn(),
    openTargetEditor: vi.fn(),
    closeTargetEditor: vi.fn(),
    setTargetDraft: vi.fn(),
    saveTarget: vi.fn(),
    clearTarget: vi.fn(),
    decidedRecommendation: signal<RecommendationDto | null>(null),
    decideRecommendation: vi.fn(),
  };
}

export type MockDossierStore = ReturnType<typeof createMockStore>;

export const buildMockStore: () => MockDossierStore = createMockStore;
