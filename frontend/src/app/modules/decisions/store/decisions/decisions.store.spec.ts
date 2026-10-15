import {HttpErrorResponse} from '@angular/common/http';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {NEVER, of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {type RecommendationDto} from '../../../../shared/models/recommendation/recommendation.model';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {OFFLINE_ERROR_MESSAGE} from '../../../../shared/utils/async-view.utils';
import {DECISIONS_LOAD_FAILED_MESSAGE} from '../../constants/recommendation/recommendation.constants';
import {DecisionSheetService} from '../../services/decision-sheet.service';
import {RecommendationService} from '../../services/recommendation.service';
import {ScorecardService} from '../../services/scorecard.service';
import {DecisionsStore} from './decisions.store';

function mkRecommendation(overrides: Partial<RecommendationDto> = {}): RecommendationDto {
  return {
    id: 'rec-1',
    ticker: 'GRAB',
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
    orderText: 'SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR',
    createdAt: '2026-10-12T09:00:00Z',
    expiresAt: '2026-10-19T09:00:00Z',
    decidedAt: null,
    decisionNote: null,
    execution: 'Pending',
    executedAt: null,
    subjectReturnPct: null,
    benchmarkReturnPct: null,
    excessReturnPct: null,
    outcomeScoredAt: null,
    ...overrides,
  };
}
describe('DecisionsStore', () => {
  const offline = signal(false);
  let service: {list: ReturnType<typeof vi.fn>};
  let sheet: {open: ReturnType<typeof vi.fn>};
  let scorecards: {read: ReturnType<typeof vi.fn>};

  function inject(): InstanceType<typeof DecisionsStore> {
    TestBed.configureTestingModule({
      providers: [
        DecisionsStore,
        {provide: RecommendationService, useValue: service},
        {provide: DecisionSheetService, useValue: sheet},
        {provide: ScorecardService, useValue: scorecards},
        {provide: ConnectivityService, useValue: {offline}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    return TestBed.inject(DecisionsStore);
  }

  beforeEach(() => {
    TestBed.resetTestingModule();
    offline.set(false);
    service = {list: vi.fn().mockReturnValue(of({pending: [], decided: []}))};
    sheet = {open: vi.fn().mockReturnValue(of(undefined))};
    scorecards = {
      read: vi.fn((symbol: string) => of(ScorecardFixtures.scorecard({symbol}))),
    };
  });

  it('reads one scorecard per pending ticker and keys it by symbol', () => {
    service.list.mockReturnValue(
      of({
        pending: [
          mkRecommendation({id: 'a', ticker: 'GRAB'}),
          mkRecommendation({id: 'b', ticker: 'DRAM'}),
          mkRecommendation({id: 'c', ticker: 'GRAB'}),
        ],
        decided: [mkRecommendation({id: 'd', ticker: 'NVDA', status: 'Rejected'})],
      })
    );
    const store = inject();

    expect(scorecards.read.mock.calls.map(([symbol]) => symbol)).toEqual(['GRAB', 'DRAM']);
    expect(Object.keys(store.scorecards()).sort()).toEqual(['DRAM', 'GRAB']);
    expect(store.scorecards()['GRAB'].verdict.word).toBe('Avoid');
  });

  it('leaves a card without a verdict when its scorecard read fails', () => {
    service.list.mockReturnValue(
      of({
        pending: [
          mkRecommendation({id: 'a', ticker: 'GRAB'}),
          mkRecommendation({id: 'b', ticker: 'DRAM'}),
        ],
        decided: [],
      })
    );
    scorecards.read.mockImplementation((symbol: string) =>
      symbol === 'GRAB'
        ? throwError(() => new HttpErrorResponse({status: 500}))
        : of(ScorecardFixtures.scorecard({symbol}))
    );
    const store = inject();

    expect(Object.keys(store.scorecards())).toEqual(['DRAM']);
    expect(store.view().status).toBe('success');
  });

  it('reads no scorecard for an empty queue', () => {
    inject();

    expect(scorecards.read).not.toHaveBeenCalled();
  });

  it('loads on open and is loading until the queue arrives', () => {
    service.list.mockReturnValue(NEVER);
    const store = inject();

    expect(service.list).toHaveBeenCalledOnce();
    expect(store.view().status).toBe('loading');
  });

  it('orders pending oldest first and keeps decided as served', () => {
    const newer = mkRecommendation({id: 'b', ticker: 'GRAB', createdAt: '2026-10-12T00:00:00Z'});
    const older = mkRecommendation({id: 'a', ticker: 'DRAM', createdAt: '2026-10-01T00:00:00Z'});
    const decided = mkRecommendation({id: 'c', status: 'Rejected'});
    service.list.mockReturnValue(of({pending: [newer, older], decided: [decided]}));
    const store = inject();

    expect(store.pending().map(r => r.ticker)).toEqual(['DRAM', 'GRAB']);
    expect(store.decided()).toEqual([decided]);
    expect(store.hasData()).toBe(true);
    expect(store.view().status).toBe('success');
  });

  it('reads an empty queue as no data', () => {
    const store = inject();

    expect(store.hasData()).toBe(false);
    expect(store.view().status).toBe('success');
  });

  it('resolves a registered error and falls back to its own message otherwise', () => {
    service.list.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 404, error: {errorCode: 'RECOMMENDATION_NOT_FOUND'}})
      )
    );
    const store = inject();
    expect(store.view().errorMessage).toBe(ERROR_MESSAGES_REGISTRY['RECOMMENDATION_NOT_FOUND']);

    service.list.mockReturnValue(throwError(() => new Error('boom')));
    store.load();
    expect(store.view().errorMessage).toBe(DECISIONS_LOAD_FAILED_MESSAGE);
  });

  it('counts offline only while nothing is on screen', () => {
    offline.set(true);
    service.list.mockReturnValue(throwError(() => new Error('offline')));
    const empty = inject();
    expect(empty.view().errorMessage).toBe(OFFLINE_ERROR_MESSAGE);

    TestBed.resetTestingModule();
    service.list.mockReturnValue(of({pending: [mkRecommendation()], decided: []}));
    const withRows = inject();
    expect(withRows.view()).toEqual({status: 'success', errorMessage: '', offlineNotice: null});
  });

  it('moves a recorded verdict from pending to the head of decided', () => {
    const first = mkRecommendation({id: 'a'});
    const second = mkRecommendation({id: 'b', createdAt: '2026-10-13T00:00:00Z'});
    const earlier = mkRecommendation({id: 'z', status: 'Expired'});
    service.list.mockReturnValue(of({pending: [first, second], decided: [earlier]}));
    const decided = {...first, status: 'Confirmed' as const};
    sheet.open.mockReturnValue(of(decided));
    const store = inject();

    store.decide({recommendation: first, verdict: 'confirm'});

    expect(sheet.open).toHaveBeenCalledWith(first, 'confirm');
    expect(store.pending().map(r => r.id)).toEqual(['b']);
    expect(store.decided().map(r => r.id)).toEqual(['a', 'z']);
  });

  it('leaves the queue alone when the sheet is dismissed', () => {
    const pending = mkRecommendation();
    service.list.mockReturnValue(of({pending: [pending], decided: []}));
    const store = inject();

    store.decide({recommendation: pending, verdict: 'reject'});

    expect(store.pending()).toEqual([pending]);
    expect(store.decided()).toEqual([]);
  });
});
