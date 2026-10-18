import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {AuthStore} from '../../auth/store/auth.store';
import {DecisionSheetService} from '../../decisions/services/decision-sheet.service';
import {
  type AssetChartDto,
  type AssetDossierDto,
  type AssetLedgerReadDto,
  type DossierQuoteDto,
  type DossierUserTargetDto,
  type ValuationSnapshotDto,
} from '../models/dossier/dossier.model';
import {DossierService} from '../services/dossier.service';
import {DossierStore} from './dossier.store';

const DOSSIER: AssetDossierDto = {
  symbol: 'AAPL',
  position: {
    provider: 'ibkr',
    assetClass: 'Equities',
    quantity: 1,
    currentValueUsd: 100,
    costBasisUsd: null,
    unrealizedPnlUsd: null,
    unrealizedPnlPercent: null,
    shareOfBookPercent: null,
    taxLots: [],
  },
  thesis: null,
  valuation: null,
  analysts: null,
  recentNews: [],
  nextEarnings: null,
  radarSignals: [],
  generatedAt: '2026-09-01T10:00:00Z',
};

const READ: AssetLedgerReadDto = {
  symbol: 'AAPL',
  narrative: 'A read.',
  generatedAt: '2026-09-03T00:00:00Z',
  isStale: false,
  cached: true,
};

const QUOTE: DossierQuoteDto = {
  ticker: 'AAPL',
  price: 189.3,
  previousClose: 187,
  changePct: 1.23,
  currency: 'USD',
};

const CHART: AssetChartDto = {
  symbol: 'AAPL',
  range: '1Y',
  currency: 'USD',
  points: [{date: '2026-10-13', close: 189.3, benchmark: 100}],
  benchmarkLabel: 'S&P 500',
  trades: [],
  averageCost: null,
  changePct: 1,
  benchmarkChangePct: 1,
  source: 'yahoo-chart',
  retrievedAt: '2026-10-13T00:00:00Z',
};

const TARGET: DossierUserTargetDto = {
  targetPrice: 250,
  currency: 'USD',
  updatedAt: '2026-10-13T00:00:00Z',
};

const NO_METRIC = {
  value: null,
  fiveYearAvg: null,
  historyWindowYears: null,
  historyUnavailable: true,
};

function majorUnitValuation(currency: string): ValuationSnapshotDto {
  return {
    ticker: 'AAPL',
    notApplicable: false,
    price: 26.4,
    isStale: false,
    metrics: {
      trailingPe: NO_METRIC,
      forwardPe: NO_METRIC,
      evToEbitda: NO_METRIC,
      dividendYield: NO_METRIC,
    },
    consensusTarget: 30,
    impliedUpsidePct: null,
    peerSet: null,
    sources: [],
    retrievedAt: '2026-10-13T00:00:00Z',
    name: null,
    sector: null,
    industry: null,
    priceAsOf: null,
    currency,
    targetLow: 24,
    targetHigh: 36,
  };
}

function mkRecommendation(overrides: Partial<RecommendationDto> = {}): RecommendationDto {
  return {
    id: 'rec-1',
    ticker: 'AAPL',
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
    sizeShares: 11,
    shareOfBookBeforePct: 12.2,
    shareOfBookAfterPct: 9.5,
    limitPrice: 189.3,
    limitCurrency: 'USD',
    venue: 'IBKR',
    orderText: 'SELL 11 AAPL / LIMIT 189.30 USD DAY / IBKR',
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

function httpError(errorCode: string) {
  return throwError(() => new HttpErrorResponse({status: 500, error: {errorCode}}));
}

describe('DossierStore', () => {
  let service: {
    getDossier: ReturnType<typeof vi.fn>;
    getQuotes: ReturnType<typeof vi.fn>;
    getLedgerRead: ReturnType<typeof vi.fn>;
    generateLedgerRead: ReturnType<typeof vi.fn>;
    getChart: ReturnType<typeof vi.fn>;
    setTarget: ReturnType<typeof vi.fn>;
    clearTarget: ReturnType<typeof vi.fn>;
  };
  let canUseAi: boolean;
  let sheet: {open: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof DossierStore> {
    TestBed.configureTestingModule({
      providers: [
        DossierStore,
        {provide: DossierService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {
          provide: ActivatedRoute,
          useValue: {snapshot: {paramMap: convertToParamMap({symbol: 'AAPL'}), fragment: null}},
        },
        {provide: AuthStore, useValue: {canUseAi: () => canUseAi}},
        {provide: DecisionSheetService, useValue: sheet},
      ],
    });
    return TestBed.inject(DossierStore);
  }

  beforeEach(() => {
    canUseAi = true;
    sheet = {open: vi.fn().mockReturnValue(of(undefined))};
    service = {
      getDossier: vi.fn().mockReturnValue(of(DOSSIER)),
      getQuotes: vi.fn().mockReturnValue(of([QUOTE])),
      getLedgerRead: vi.fn().mockReturnValue(of(READ)),
      generateLedgerRead: vi.fn().mockReturnValue(of(READ)),
      getChart: vi.fn().mockImplementation((_: string, range: string) => of({...CHART, range})),
      setTarget: vi.fn().mockReturnValue(of(TARGET)),
      clearTarget: vi.fn().mockReturnValue(of(undefined)),
    };
  });

  it('is loading until the dossier arrives', () => {
    service.getDossier.mockReturnValue(NEVER);

    const store = inject();

    expect(store.dossierStatus()).toBe('loading');
    expect(store.isDossierLoading()).toBe(true);
  });

  it('reports success for all three requests once everything loaded', () => {
    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.ledgerReadStatus()).toBe('success');
    expect(store.quoteStatus()).toBe('success');
    expect(store.dossier()).toEqual(DOSSIER);
    expect(store.ledgerRead()).toEqual(READ);
    expect(store.quoteHeader()?.priceText).toBe('$189.30');
  });

  it.each([
    ['GBp', 'GBP', '£26.50'],
    ['ILA', 'ILS', 'ILS 26.50'],
  ])(
    'puts a %s quote in %s so the price, header and target band share the dossier unit',
    (minor, major, priceText) => {
      service.getDossier.mockReturnValue(
        of({
          ...DOSSIER,
          valuation: majorUnitValuation(major),
          userTarget: {targetPrice: 32, currency: major, updatedAt: '2026-10-13T00:00:00Z'},
        })
      );
      service.getQuotes.mockReturnValue(
        of([{...QUOTE, price: 2650, previousClose: 2600, currency: minor}])
      );

      const store = inject();

      expect(store.quote()).toMatchObject({price: 26.5, previousClose: 26, currency: major});
      expect(store.currentPrice()).toBe(26.5);
      expect(store.quoteHeader()?.priceText).toBe(priceText);
      const band = store.targetBand();
      expect(band?.markers.find(m => m.key === 'price')?.value).toBe(26.5);
      expect(band?.markers.every(m => m.leftPct >= 0 && m.leftPct <= 100)).toBe(true);
    }
  );

  it('keeps the quote and ledger read idle when they are not requested', () => {
    canUseAi = false;
    service.getDossier.mockReturnValue(of({...DOSSIER, position: null}));

    const store = inject();

    expect(store.ledgerReadStatus()).toBe('idle');
    expect(store.quoteStatus()).toBe('idle');
    expect(service.getLedgerRead).not.toHaveBeenCalled();
    expect(service.getQuotes).not.toHaveBeenCalled();
  });

  it('shows the default message for an unregistered dossier error code', () => {
    service.getDossier.mockReturnValue(httpError('UNKNOWN'));

    const store = inject();

    expect(store.dossierStatus()).toBe('error');
    expect(store.dossierErrorMessage()).toBe('Failed to load asset dossier.');
  });

  it('keeps a failed ledger read from touching the dossier and resolves its message', () => {
    service.getLedgerRead.mockReturnValue(httpError('LEDGER_READ_UNAVAILABLE'));

    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.dossierErrorMessage()).toBe('');
    expect(store.ledgerReadErrorMessage()).toBe(
      'Ledger could not produce a read right now. Try again shortly.'
    );
  });

  it('falls back to the default message for an unregistered ledger read code', () => {
    service.getLedgerRead.mockReturnValue(httpError('NOPE_XYZ'));

    const store = inject();

    expect(store.ledgerReadErrorMessage()).toBe("Failed to load Ledger's read.");
  });

  it('clears the quote and flags the error without raising a dossier error when the quote fails', () => {
    service.getQuotes.mockReturnValue(throwError(() => new Error('boom')));

    const store = inject();

    expect(store.quote()).toBeNull();
    expect(store.quoteStatus()).toBe('error');
    expect(store.dossierStatus()).toBe('success');
    expect(store.dossierErrorMessage()).toBe('');
  });

  it('keeps the cached read visible while a regeneration is in flight', () => {
    const store = inject();
    service.generateLedgerRead.mockReturnValue(NEVER);

    store.generateLedgerRead({symbol: 'AAPL', force: true});

    expect(store.isLedgerReadLoading()).toBe(true);
    expect(store.ledgerRead()).toEqual(READ);
  });

  it('loads the one-year chart with the dossier and reloads on a new range only', () => {
    const store = inject();

    expect(service.getChart).toHaveBeenCalledWith('AAPL', '1Y');
    expect(store.chartStatus()).toBe('success');
    expect(store.chart()?.range).toBe('1Y');

    store.selectChartRange('1Y');
    expect(service.getChart).toHaveBeenCalledTimes(1);

    store.selectChartRange('5Y');
    expect(service.getChart).toHaveBeenLastCalledWith('AAPL', '5Y');
    expect(store.chartRange()).toBe('5Y');
    expect(store.chart()?.range).toBe('5Y');
  });

  it('keeps the page when the chart fails and resolves its message', () => {
    service.getChart.mockReturnValue(httpError('INVALID_CHART_RANGE'));

    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.chartStatus()).toBe('error');
    expect(store.chartAsyncStatus()).toBe('error');
  });

  it('ignores a range change before the dossier has loaded', () => {
    service.getDossier.mockReturnValue(NEVER);
    const store = inject();

    store.selectChartRange('5Y');

    expect(service.getChart).not.toHaveBeenCalled();
    expect(store.chartRange()).toBe('1Y');
  });

  it('saves a valid draft in the asset currency and writes it into the dossier', () => {
    service.getDossier.mockReturnValue(
      of({
        ...DOSSIER,
        performance: {
          lastClose: 189,
          yearChangePct: null,
          high52Week: null,
          low52Week: null,
          fromHighPct: null,
          benchmarkYearChangePct: null,
          currency: 'EUR',
          asOf: null,
        },
      })
    );
    const store = inject();

    store.openTargetEditor();
    store.setTargetDraft('250');
    store.saveTarget();

    expect(service.setTarget).toHaveBeenCalledWith('AAPL', {targetPrice: 250, currency: 'EUR'});
    expect(store.dossier()?.userTarget).toEqual(TARGET);
    expect(store.isEditingTarget()).toBe(false);
    expect(store.targetStatus()).toBe('success');
  });

  it('sends no currency when the asset has none on record', () => {
    const store = inject();

    store.setTargetDraft('250');
    store.saveTarget();

    expect(service.setTarget).toHaveBeenCalledWith('AAPL', {targetPrice: 250, currency: null});
  });

  it('never sends an invalid draft', () => {
    const store = inject();

    store.setTargetDraft('-1');
    store.saveTarget();

    expect(service.setTarget).not.toHaveBeenCalled();
  });

  it('keeps the editor open and resolves the message when a save fails', () => {
    service.setTarget.mockReturnValue(httpError('INVALID_PRICE_TARGET'));
    const store = inject();

    store.openTargetEditor();
    store.setTargetDraft('250');
    store.saveTarget();

    expect(store.targetStatus()).toBe('error');
    expect(store.targetErrorMessage()).not.toBe('');
    expect(store.isEditingTarget()).toBe(true);
  });

  it('clears the target', () => {
    service.getDossier.mockReturnValue(of({...DOSSIER, userTarget: TARGET}));
    const store = inject();

    store.clearTarget();

    expect(service.clearTarget).toHaveBeenCalledWith('AAPL');
    expect(store.dossier()?.userTarget).toBeNull();
  });

  it('flags a failed clear and does nothing before the dossier has loaded', () => {
    service.clearTarget.mockReturnValue(httpError('NOPE'));
    const store = inject();

    store.clearTarget();
    expect(store.targetErrorMessage()).toBe('Failed to save your target.');
  });

  it('does not clear before the dossier has loaded', () => {
    service.getDossier.mockReturnValue(NEVER);
    const store = inject();

    store.clearTarget();
    store.saveTarget();

    expect(service.clearTarget).not.toHaveBeenCalled();
    expect(service.setTarget).not.toHaveBeenCalled();
  });

  it('records a verdict from the sheet in place of the pending proposal', () => {
    const pending = mkRecommendation();
    const decided = mkRecommendation({status: 'Rejected', decisionNote: 'Still a core holding.'});
    service.getDossier.mockReturnValue(of({...DOSSIER, pendingRecommendation: pending}));
    sheet.open.mockReturnValue(of(decided));
    const store = inject();

    store.decideRecommendation({recommendation: pending, verdict: 'reject'});

    expect(sheet.open).toHaveBeenCalledWith(pending, 'reject');
    expect(store.dossier()?.pendingRecommendation).toBeNull();
    expect(store.decidedRecommendation()).toEqual(decided);
  });

  it('keeps the pending proposal when the sheet is dismissed', () => {
    const pending = mkRecommendation();
    service.getDossier.mockReturnValue(of({...DOSSIER, pendingRecommendation: pending}));
    const store = inject();

    store.decideRecommendation({recommendation: pending, verdict: 'confirm'});

    expect(store.dossier()?.pendingRecommendation).toEqual(pending);
    expect(store.decidedRecommendation()).toBeNull();
  });
});
