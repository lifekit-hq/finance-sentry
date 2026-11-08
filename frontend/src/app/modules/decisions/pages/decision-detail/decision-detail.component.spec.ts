import {TestBed} from '@angular/core/testing';
import {ActivatedRoute, convertToParamMap, provideRouter} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {EMPTY, NEVER, of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {LEDGER_DECISION_ID_PARAM} from '../../../../shared/enums/app-route/app-route.enum';
import {
  type RecommendationDto,
  type RecommendationsResult,
} from '../../../../shared/models/recommendation/recommendation.model';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {DECISION_DETAIL_MISSING_COPY} from '../../constants/recommendation/recommendation.constants';
import {DecisionSheetService} from '../../services/decision-sheet.service';
import {RecommendationService} from '../../services/recommendation.service';
import {ScorecardService} from '../../services/scorecard.service';
import {DecisionDetailComponent} from './decision-detail.component';

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
    decisionValuePct: null,
    outcomeScoredAt: null,
    ...overrides,
  };
}

describe('DecisionDetailComponent', () => {
  let list: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let read: ReturnType<typeof vi.fn>;

  const render = (id: string, result: RecommendationsResult) => {
    list.mockReturnValue(of(result));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {snapshot: {paramMap: convertToParamMap({[LEDGER_DECISION_ID_PARAM]: id})}},
        },
        {provide: RecommendationService, useValue: {list}},
        {provide: DecisionSheetService, useValue: {open}},
        {provide: ScorecardService, useValue: {read}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    const fixture = TestBed.createComponent(DecisionDetailComponent);
    fixture.detectChanges();
    return {fixture, host: fixture.nativeElement as HTMLElement};
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    list = vi.fn().mockReturnValue(NEVER);
    open = vi.fn().mockReturnValue(of(undefined));
    read = vi.fn().mockReturnValue(EMPTY);
  });

  it('shows the whole proposal with Reject and Confirm pinned outside the scrolling record', () => {
    const {host} = render('rec-1', {pending: [mkRecommendation()], decided: []});

    expect(host.querySelector('[data-testid="recommendation-card-GRAB"]')).not.toBeNull();
    const actions = host.querySelector('[data-testid="decision-actions"]');
    expect(actions?.querySelector('[data-testid="recommendation-confirm"]')).not.toBeNull();
    expect(actions?.closest('cmn-page-scroll')).toBeNull();
    expect(host.querySelector('cmn-page-scroll [data-testid="recommendation-confirm"]')).toBeNull();
  });

  it('opens the sheet for the verdict pressed in the pinned bar', () => {
    const pending = mkRecommendation();
    const {host} = render('rec-1', {pending: [pending], decided: []});

    host.querySelector<HTMLElement>('[data-testid="recommendation-reject"] button')?.click();

    expect(open).toHaveBeenCalledWith(pending, 'reject');
  });

  it("reads only the open proposal's research verdict", () => {
    read.mockImplementation((symbol: string) => of(ScorecardFixtures.scorecard({symbol})));
    const {host} = render('rec-1', {
      pending: [mkRecommendation(), mkRecommendation({id: 'rec-2', ticker: 'DRAM'})],
      decided: [],
    });

    expect(read).toHaveBeenCalledTimes(1);
    expect(read).toHaveBeenCalledWith('GRAB');
    expect(host.querySelector('[data-testid="recommendation-verdict"]')).not.toBeNull();
  });

  it('swaps the pinned bar for the recorded outcome once the proposal is decided', () => {
    const {host} = render('rec-1', {
      pending: [],
      decided: [
        mkRecommendation({
          status: 'Confirmed',
          execution: 'Executed',
          decidedAt: '2026-10-13T00:00:00Z',
        }),
      ],
    });

    expect(host.querySelector('[data-testid="decision-actions"]')).toBeNull();
    expect(host.querySelector('[data-testid="decision-recorded"]')?.textContent).toContain(
      'Placed at the broker'
    );
  });

  it('says so when the id names no proposal in the queue', () => {
    const {host} = render('gone', {pending: [mkRecommendation()], decided: []});

    expect(host.textContent).toContain(DECISION_DETAIL_MISSING_COPY.message);
    expect(host.querySelector('[data-testid="decision-actions"]')).toBeNull();
  });
});
