import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, type ParamMap} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {BehaviorSubject, NEVER, of, Subject, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../core/errors/error-messages.registry';
import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {ScorecardFixtures} from '../../../shared/models/scorecard/scorecard.fixtures';
import {ThesisRevisionFixtures} from '../../../shared/models/thesis-revision/thesis-revision.fixtures';
import {AppPreferencesStore} from '../../../shared/store/preferences/app-preferences.store';
import {AuthStore} from '../../auth/store/auth.store';
import {DecisionSheetService} from '../../decisions/services/decision-sheet.service';
import {ScorecardService} from '../../decisions/services/scorecard.service';
import {ThesisRevisionService} from '../../decisions/services/thesis-revision.service';
import {ThesisRevisionSheetService} from '../../decisions/services/thesis-revision-sheet.service';
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
  let preferences: {preferences: ReturnType<typeof signal>; patch: ReturnType<typeof vi.fn>};
  let params: BehaviorSubject<ParamMap>;
  let sheet: {open: ReturnType<typeof vi.fn>};
  let revisions: {list: ReturnType<typeof vi.fn>};
  let revisionSheet: {open: ReturnType<typeof vi.fn>};
  let scorecards: {read: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof DossierStore> {
    TestBed.configureTestingModule({
      providers: [
        DossierStore,
        {provide: DossierService, useValue: service},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
        {
          provide: ActivatedRoute,
          useValue: {paramMap: params, snapshot: {fragment: null}},
        },
        {provide: AuthStore, useValue: {canUseAi: () => canUseAi}},
        {provide: AppPreferencesStore, useValue: preferences},
        {provide: DecisionSheetService, useValue: sheet},
        {provide: ThesisRevisionService, useValue: revisions},
        {provide: ThesisRevisionSheetService, useValue: revisionSheet},
        {provide: ScorecardService, useValue: scorecards},
      ],
    });
    return TestBed.inject(DossierStore);
  }

  beforeEach(() => {
    canUseAi = true;
    preferences = {preferences: signal(null), patch: vi.fn().mockResolvedValue(true)};
    params = new BehaviorSubject(convertToParamMap({symbol: 'AAPL'}));
    sheet = {open: vi.fn().mockReturnValue(of(undefined))};
    revisions = {list: vi.fn().mockReturnValue(of({pending: [], decided: []}))};
    revisionSheet = {open: vi.fn().mockReturnValue(of(undefined))};
    scorecards = {read: vi.fn().mockReturnValue(of(ScorecardFixtures.scorecard({symbol: 'AAPL'})))};
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

  it('drops the old symbol and loads the new one when the route symbol changes in place', () => {
    const store = inject();
    TestBed.tick();
    expect(store.dossier()?.symbol).toBe('AAPL');
    expect(store.ledgerRead()).toEqual(READ);

    service.getDossier.mockReturnValue(NEVER);
    service.getLedgerRead.mockReturnValue(NEVER);
    params.next(convertToParamMap({symbol: 'ZZZZ'}));
    TestBed.tick();

    expect(service.getDossier).toHaveBeenLastCalledWith('ZZZZ');
    expect(service.getLedgerRead).toHaveBeenLastCalledWith('ZZZZ');
    expect(store.dossier()).toBeNull();
    expect(store.ledgerRead()).toBeNull();
    expect(store.quote()).toBeNull();
    expect(store.chart()).toBeNull();
  });

  it('drops a ledger read still being generated for the old symbol when the symbol changes', () => {
    const store = inject();
    TestBed.tick();
    const pending = new Subject<AssetLedgerReadDto>();
    service.generateLedgerRead.mockReturnValue(pending);
    store.generateLedgerRead({symbol: 'AAPL', force: true});

    service.getDossier.mockReturnValue(NEVER);
    service.getLedgerRead.mockReturnValue(NEVER);
    params.next(convertToParamMap({symbol: 'ZZZZ'}));
    TestBed.tick();
    pending.next(READ);

    expect(store.ledgerRead()).toBeNull();
  });

  it('returns the per-symbol request statuses to idle when the symbol changes mid-request', () => {
    const store = inject();
    TestBed.tick();
    service.setTarget.mockReturnValue(NEVER);
    store.openTargetEditor();
    store.setTargetDraft('200');
    store.saveTarget();
    expect(store.targetStatus()).toBe('loading');
    service.generateLedgerRead.mockReturnValue(NEVER);
    store.generateLedgerRead({symbol: 'AAPL', force: true});
    expect(store.ledgerReadStatus()).toBe('loading');

    // No AI access, so the new symbol starts no ledger read of its own and the status stays observable.
    canUseAi = false;
    service.getDossier.mockReturnValue(NEVER);
    params.next(convertToParamMap({symbol: 'ZZZZ'}));
    TestBed.tick();

    expect(store.targetStatus()).toBe('idle');
    expect(store.quoteStatus()).toBe('idle');
    expect(store.chartStatus()).toBe('idle');
    expect(store.ledgerReadStatus()).toBe('idle');
  });

  it('drops a decision still open on the old symbol when the symbol changes', () => {
    const pending = mkRecommendation();
    const decided = mkRecommendation({status: 'Rejected', decisionNote: 'Still a core holding.'});
    service.getDossier.mockReturnValue(of({...DOSSIER, pendingRecommendation: pending}));
    const decision = new Subject<RecommendationDto>();
    sheet.open.mockReturnValue(decision);
    const store = inject();
    TestBed.tick();
    store.decideRecommendation({recommendation: pending, verdict: 'reject'});

    service.getDossier.mockReturnValue(NEVER);
    service.getLedgerRead.mockReturnValue(NEVER);
    params.next(convertToParamMap({symbol: 'ZZZZ'}));
    TestBed.tick();
    decision.next(decided);

    expect(store.decidedRecommendation()).toBeNull();
  });

  it('does not reload when the route re-emits the same symbol', () => {
    inject();
    TestBed.tick();
    params.next(convertToParamMap({symbol: 'AAPL'}));
    TestBed.tick();

    expect(service.getDossier).toHaveBeenCalledTimes(1);
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

  it('loads the whole price history once with the dossier', () => {
    const store = inject();

    expect(service.getChart).toHaveBeenCalledOnce();
    expect(service.getChart).toHaveBeenCalledWith('AAPL', 'MAX');
    expect(store.chartStatus()).toBe('success');
  });

  it('keeps the page when the chart fails and resolves its message', () => {
    service.getChart.mockReturnValue(httpError('INVALID_CHART_RANGE'));

    const store = inject();

    expect(store.dossierStatus()).toBe('success');
    expect(store.chartStatus()).toBe('error');
    expect(store.chartAsyncStatus()).toBe('error');
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

  describe('the verdict gap (issue #1414)', () => {
    const unreadable: AssetDossierDto = {...DOSSIER, scorecard: null, scorecardGap: 'unreadable'};

    it('names the reason the dossier carries and offers the retry only for an unreadable read', () => {
      service.getDossier.mockReturnValue(of(unreadable));
      const store = inject();
      TestBed.tick();

      expect(store.verdictGap()).toBe('unreadable');
      expect(store.verdictAsyncStatus()).toBe('error');
    });

    it('reads the scorecard again on retry and swaps the gap for the verdict, without reloading the page', () => {
      service.getDossier.mockReturnValue(of(unreadable));
      const store = inject();
      TestBed.tick();

      store.retryVerdict('AAPL');

      expect(scorecards.read).toHaveBeenCalledWith('AAPL');
      expect(store.dossier()?.scorecard?.symbol).toBe('AAPL');
      expect(store.verdictGap()).toBeNull();
      expect(service.getDossier).toHaveBeenCalledTimes(1);
    });

    it('keeps the gap and the retry when the retry fails too', () => {
      service.getDossier.mockReturnValue(of(unreadable));
      scorecards.read.mockReturnValue(httpError('SOME_UNREGISTERED_CODE'));
      const store = inject();
      TestBed.tick();

      store.retryVerdict('AAPL');

      expect(store.verdictGap()).toBe('unreadable');
      expect(store.verdictAsyncStatus()).toBe('error');
      expect(store.dossier()?.symbol).toBe('AAPL');
    });
  });

  describe('thesis change', () => {
    const REVISION = ThesisRevisionFixtures.revision({ticker: 'AAPL'});

    it("picks this ticker's pending change out of the owner's queue", () => {
      revisions.list.mockReturnValue(
        of({
          pending: [ThesisRevisionFixtures.revision({id: 'other', ticker: 'NVDA'}), REVISION],
          decided: [],
        })
      );
      const store = inject();
      TestBed.tick();

      expect(store.pendingRevision()).toEqual(REVISION);
    });

    it('reads no queue for someone without Ledger', () => {
      canUseAi = false;
      const store = inject();
      TestBed.tick();

      expect(revisions.list).not.toHaveBeenCalled();
      expect(store.pendingRevision()).toBeNull();
    });

    it('leaves the page without the card, and without an error, when the queue cannot be read', () => {
      revisions.list.mockReturnValue(throwError(() => new HttpErrorResponse({status: 500})));
      const store = inject();
      TestBed.tick();

      expect(store.pendingRevision()).toBeNull();
      expect(store.dossier()?.symbol).toBe('AAPL');
      expect(store.dossierStatus()).toBe('success');
    });

    it('writes the confirmed thesis onto the page and keeps the verdict in the card place', () => {
      const written = ThesisRevisionFixtures.thesis({id: 'new', ticker: 'AAPL'});
      const confirmed = {...REVISION, status: 'Confirmed' as const};
      revisions.list.mockReturnValue(of({pending: [REVISION], decided: []}));
      revisionSheet.open.mockReturnValue(
        of({revision: confirmed, thesis: written, gate: null, note: null})
      );
      const store = inject();
      TestBed.tick();

      store.decideRevision({revision: REVISION, verdict: 'confirm'});

      expect(revisionSheet.open).toHaveBeenCalledWith(REVISION, 'confirm');
      expect(store.pendingRevision()).toBeNull();
      expect(store.decidedRevision()).toEqual(confirmed);
      expect(store.dossier()?.thesis).toEqual(written);
    });

    it('leaves the thesis on the page when the change is rejected', () => {
      const rejected = {...REVISION, status: 'Rejected' as const, decisionNote: 'No.'};
      revisions.list.mockReturnValue(of({pending: [REVISION], decided: []}));
      revisionSheet.open.mockReturnValue(
        of({revision: rejected, thesis: null, gate: null, note: null})
      );
      service.getDossier.mockReturnValue(of({...DOSSIER, thesis: ThesisRevisionFixtures.thesis()}));
      const store = inject();
      TestBed.tick();

      store.decideRevision({revision: REVISION, verdict: 'reject'});

      expect(store.pendingRevision()).toBeNull();
      expect(store.decidedRevision()).toEqual(rejected);
      expect(store.dossier()?.thesis).toEqual(ThesisRevisionFixtures.thesis());
    });

    it('keeps the pending change when the sheet is dismissed', () => {
      revisions.list.mockReturnValue(of({pending: [REVISION], decided: []}));
      const store = inject();
      TestBed.tick();

      store.decideRevision({revision: REVISION, verdict: 'confirm'});

      expect(store.pendingRevision()).toEqual(REVISION);
      expect(store.decidedRevision()).toBeNull();
    });
  });

  it('saves the verdict display to the account and reads it back from there', () => {
    const store = inject();
    expect(store.verdictDisplay()).toBe('simple');

    store.setVerdictDisplay('detailed');
    preferences.preferences.set({verdictDisplay: 'detailed'});

    expect(preferences.patch).toHaveBeenCalledExactlyOnceWith({verdictDisplay: 'detailed'});
    expect(store.verdictDisplay()).toBe('detailed');
  });

  it('refuses a verdict display the account would not accept', () => {
    const store = inject();

    store.setVerdictDisplay('verbose');

    expect(preferences.patch).not.toHaveBeenCalled();
  });
});
