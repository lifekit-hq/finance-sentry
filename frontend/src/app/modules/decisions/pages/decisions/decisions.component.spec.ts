import {TestBed} from '@angular/core/testing';
import {provideRouter} from '@angular/router';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {EMPTY, NEVER, of} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {
  GAIN_TEXT_CLASS,
  LOSS_TEXT_CLASS,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {
  type RecommendationDto,
  type RecommendationsResult,
} from '../../../../shared/models/recommendation/recommendation.model';
import {ScorecardFixtures} from '../../../../shared/models/scorecard/scorecard.fixtures';
import {
  DECISION_SKELETON_CARDS,
  DECISIONS_EMPTY_COPY,
} from '../../constants/recommendation/recommendation.constants';
import {DecisionSheetService} from '../../services/decision-sheet.service';
import {RecommendationService} from '../../services/recommendation.service';
import {ScorecardService} from '../../services/scorecard.service';
import {DecisionsComponent} from './decisions.component';

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
describe('DecisionsComponent', () => {
  let list: ReturnType<typeof vi.fn>;
  let open: ReturnType<typeof vi.fn>;
  let read: ReturnType<typeof vi.fn>;

  const render = (result: RecommendationsResult) => {
    list.mockReturnValue(of(result));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: RecommendationService, useValue: {list}},
        {provide: DecisionSheetService, useValue: {open}},
        {provide: ScorecardService, useValue: {read}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    const fixture = TestBed.createComponent(DecisionsComponent);
    fixture.detectChanges();
    return {fixture, host: fixture.nativeElement as HTMLElement};
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    list = vi.fn().mockReturnValue(NEVER);
    open = vi.fn().mockReturnValue(of(undefined));
    read = vi.fn().mockReturnValue(EMPTY);
  });

  it("opens each pending card's research verdict once its scorecard arrives", () => {
    read.mockImplementation((symbol: string) =>
      symbol === 'GRAB' ? of(ScorecardFixtures.scorecard({symbol})) : EMPTY
    );
    const {host} = render({
      pending: [
        mkRecommendation({id: 'a', ticker: 'GRAB'}),
        mkRecommendation({id: 'b', ticker: 'DRAM'}),
      ],
      decided: [],
    });

    const verdictOf = (ticker: string): Nullable<Element> =>
      host.querySelector(
        `[data-testid="recommendation-card-${ticker}"] [data-testid="recommendation-verdict"]`
      );
    expect(
      verdictOf('GRAB')?.querySelector('[data-testid="verdict-word"]')?.textContent?.trim()
    ).toBe('AVOID');
    expect(verdictOf('DRAM')).toBeNull();
  });

  it('says plainly that Finance Sentry never trades', () => {
    const {host} = render({pending: [], decided: []});

    expect(host.textContent).toContain('Finance Sentry never trades');
  });

  it('holds the place of the pending cards and the decided list while the queue loads', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {provide: RecommendationService, useValue: {list}},
        {provide: DecisionSheetService, useValue: {open}},
        {provide: ScorecardService, useValue: {read}},
        {provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY},
      ],
    });
    const fixture = TestBed.createComponent(DecisionsComponent);
    fixture.detectChanges();
    const skeleton = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="decisions-skeleton"]'
    );

    expect(skeleton?.querySelectorAll('cmn-card').length).toBe(DECISION_SKELETON_CARDS);
    expect(skeleton?.textContent).not.toContain(DECISIONS_EMPTY_COPY.message);
  });

  it('stays silent with nothing proposed', () => {
    const {host} = render({pending: [], decided: []});

    expect(host.textContent).toContain(DECISIONS_EMPTY_COPY.message);
    expect(host.querySelector('fns-recommendation-card')).toBeNull();
  });

  it('lists pending proposals oldest first, each with its ticker linked', () => {
    const {host} = render({
      pending: [
        mkRecommendation({id: 'b', ticker: 'GRAB', createdAt: '2026-10-12T00:00:00Z'}),
        mkRecommendation({id: 'a', ticker: 'DRAM', createdAt: '2026-10-01T00:00:00Z'}),
      ],
      decided: [],
    });

    const cards = Array.from(host.querySelectorAll('[data-testid^="recommendation-card-"]'));
    expect(cards.map(c => c.getAttribute('data-testid'))).toEqual([
      'recommendation-card-DRAM',
      'recommendation-card-GRAB',
    ]);
    expect(cards[0].querySelector('a')?.getAttribute('href')).toBe('/assets/DRAM');
    expect(host.querySelector('[data-testid="decisions-decided"]')).toBeNull();
    expect(host.textContent).toContain('No decisions in the last 30 days.');
  });

  it('opens the sheet for the verdict picked on a card', () => {
    const pending = mkRecommendation();
    const {host} = render({pending: [pending], decided: []});

    host.querySelector<HTMLElement>('[data-testid="recommendation-confirm"] button')?.click();

    expect(open).toHaveBeenCalledWith(pending, 'confirm');
  });

  it("shows decided outcomes with the owner's decision valued against the S&P 500, toned by sign", () => {
    const {host} = render({
      pending: [],
      decided: [
        mkRecommendation({
          id: 'a',
          action: 'Add',
          status: 'Confirmed',
          execution: 'Executed',
          decidedAt: '2026-10-05T00:00:00Z',
          excessReturnPct: 3.2,
          decisionValuePct: 3.2,
        }),
        mkRecommendation({
          id: 'b',
          ticker: 'DRAM',
          action: 'Exit',
          status: 'Rejected',
          decisionNote: 'Still a core holding.',
          decidedAt: '2026-10-04T00:00:00Z',
          excessReturnPct: 1.5,
          decisionValuePct: 1.5,
        }),
        mkRecommendation({
          id: 'c',
          ticker: 'AAPL',
          action: 'Exit',
          status: 'Confirmed',
          execution: 'Executed',
          decidedAt: '2026-10-03T00:00:00Z',
          excessReturnPct: 2,
          decisionValuePct: -2,
        }),
        mkRecommendation({
          id: 'd',
          ticker: 'MSFT',
          action: 'Add',
          status: 'Rejected',
          decidedAt: '2026-10-02T00:00:00Z',
          excessReturnPct: 4,
          decisionValuePct: -4,
        }),
        mkRecommendation({
          id: 'e',
          ticker: 'AMZN',
          action: 'Trim',
          status: 'Confirmed',
          execution: 'Unexecuted',
          decidedAt: '2026-10-01T00:00:00Z',
          excessReturnPct: 6,
        }),
        mkRecommendation({
          id: 'f',
          ticker: 'KO',
          action: 'Hold',
          status: 'Rejected',
          decidedAt: '2026-09-30T00:00:00Z',
          excessReturnPct: 0.8,
        }),
        mkRecommendation({id: 'g', ticker: 'NVDA', status: 'Expired', excessReturnPct: 0}),
        mkRecommendation({id: 'h', ticker: 'META', status: 'Superseded'}),
      ],
    });

    expect(host.textContent).toContain('Nothing is waiting on you.');
    const rows = Array.from(host.querySelectorAll('[data-testid="decisions-decided"] li'));
    expect(rows).toHaveLength(8);
    expect(rows[0].textContent).toContain('Placed at the broker');
    expect(rows[0].textContent).toContain('Oct 5, 2026');
    expect(rows[1].textContent).toContain('Rejected: Still a core holding.');
    expect(rows[4].textContent).toContain('Not placed within a week');
    expect(rows[6].textContent).toContain('Oct 19, 2026');
    const excess = Array.from(host.querySelectorAll('[data-testid="decisions-excess"]'));
    expect(excess.map(e => e.textContent?.trim())).toEqual([
      'decision +3.2 pts vs S&P 500',
      'decision +1.5 pts vs S&P 500',
      'decision -2.0 pts vs S&P 500',
      'decision -4.0 pts vs S&P 500',
      'name +0.8 pts vs S&P 500',
    ]);
    expect(excess[0].className).toContain(GAIN_TEXT_CLASS);
    expect(excess[1].className).toContain(GAIN_TEXT_CLASS);
    expect(excess[2].className).toContain(LOSS_TEXT_CLASS);
    expect(excess[3].className).toContain(LOSS_TEXT_CLASS);
    expect(excess[4].className).toContain('text-text-secondary');
    expect(rows[4].querySelector('[data-testid="decisions-excess"]')).toBeNull();
    expect(rows[6].querySelector('[data-testid="decisions-excess"]')).toBeNull();
    expect(rows[7].querySelector('[data-testid="decisions-excess"]')).toBeNull();
  });
});
