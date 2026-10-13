import {describe, expect, it} from 'vitest';

import {type RecommendationDto} from '../../../shared/models/recommendation/recommendation.model';
import {RecommendationUtils} from './recommendation.utils';

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

describe('RecommendationUtils.sizeLine', () => {
  it('reads a trim as shares, dollars and the share of book it moves', () => {
    expect(RecommendationUtils.sizeLine(mkRecommendation())).toBe(
      'Sell 660 shares (about $2,080), taking it from 12.2% to 9.5% of book'
    );
  });

  it('reads an add as a buy', () => {
    expect(
      RecommendationUtils.sizeLine(
        mkRecommendation({action: 'Add', shareOfBookBeforePct: 2, shareOfBookAfterPct: 4})
      )
    ).toBe('Buy 660 shares (about $2,080), taking it from 2.0% to 4.0% of book');
  });

  it('reads an exit as selling all of it', () => {
    expect(
      RecommendationUtils.sizeLine(mkRecommendation({action: 'Exit', shareOfBookAfterPct: 0}))
    ).toBe('Sell all 660 shares (about $2,080), taking it from 12.2% to 0.0% of book');
  });

  it('keeps fractional shares', () => {
    expect(
      RecommendationUtils.sizeLine(
        mkRecommendation({sizeShares: 0.125, sizeUsd: null, shareOfBookAfterPct: null})
      )
    ).toBe('Sell 0.125 shares');
  });

  it('falls back to dollars when shares are unknown', () => {
    expect(
      RecommendationUtils.sizeLine(mkRecommendation({sizeShares: null, shareOfBookBeforePct: null}))
    ).toBe('Sell about $2,080');
  });

  it('treats zero shares as unknown', () => {
    expect(
      RecommendationUtils.sizeLine(
        mkRecommendation({sizeShares: 0, sizeUsd: null, shareOfBookBeforePct: null})
      )
    ).toBe('Sell - size not set');
  });

  it('reads an exit with no size as the whole position', () => {
    expect(
      RecommendationUtils.sizeLine(
        mkRecommendation({action: 'Exit', sizeShares: null, sizeUsd: null})
      )
    ).toBe('Sell the whole position, taking it from 12.2% to 9.5% of book');
  });

  it('reads a hold as the position kept at its share of book', () => {
    expect(RecommendationUtils.sizeLine(mkRecommendation({action: 'Hold'}))).toBe(
      'Keep the position at 12.2% of book'
    );
  });

  it('reads a hold with no share as kept as it is', () => {
    expect(
      RecommendationUtils.sizeLine(mkRecommendation({action: 'Hold', shareOfBookBeforePct: null}))
    ).toBe('Keep the position as it is');
  });
});

describe('RecommendationUtils.shareClause', () => {
  it('names both shares of book', () => {
    expect(RecommendationUtils.shareClause(mkRecommendation())).toBe(
      'taking it from 12.2% to 9.5% of book'
    );
  });

  it('is empty when a share is unknown', () => {
    expect(RecommendationUtils.shareClause(mkRecommendation({shareOfBookAfterPct: null}))).toBe('');
  });
});

describe('RecommendationUtils.outcome', () => {
  it('says an expired proposal went undecided', () => {
    expect(RecommendationUtils.outcome(mkRecommendation({status: 'Expired'}))).toBe(
      'Expired without a decision'
    );
  });

  it('says a superseded proposal was replaced', () => {
    expect(RecommendationUtils.outcome(mkRecommendation({status: 'Superseded'}))).toBe(
      'Replaced by a newer proposal'
    );
  });

  it('carries the rejection reason', () => {
    expect(
      RecommendationUtils.outcome(
        mkRecommendation({status: 'Rejected', decisionNote: 'Still a core holding.'})
      )
    ).toBe('Rejected: Still a core holding.');
  });

  it('reads a rejection with no reason on file', () => {
    expect(RecommendationUtils.outcome(mkRecommendation({status: 'Rejected'}))).toBe('Rejected');
  });

  it('is empty while pending', () => {
    expect(RecommendationUtils.outcome(mkRecommendation())).toBe('');
  });

  it('says a confirmed proposal was placed once a matching fill landed', () => {
    expect(
      RecommendationUtils.outcome(mkRecommendation({status: 'Confirmed', execution: 'Executed'}))
    ).toBe('Placed at the broker');
  });

  it('says a confirmed proposal was not placed when the window closed', () => {
    expect(
      RecommendationUtils.outcome(mkRecommendation({status: 'Confirmed', execution: 'Unexecuted'}))
    ).toBe('Not placed within a week');
  });

  it('waits for a matching fill', () => {
    expect(RecommendationUtils.outcome(mkRecommendation({status: 'Confirmed'}))).toBe(
      'Waiting for a matching fill'
    );
  });

  it('needs no order for a confirmed hold', () => {
    expect(
      RecommendationUtils.outcome(
        mkRecommendation({status: 'Confirmed', action: 'Hold', execution: 'NotApplicable'})
      )
    ).toBe('No order needed');
  });
});

describe('RecommendationUtils.excessLine', () => {
  it('is empty until scored', () => {
    expect(RecommendationUtils.excessLine(mkRecommendation())).toBe('');
  });

  it('reads an add as the name return over the S&P 500', () => {
    expect(
      RecommendationUtils.excessLine(mkRecommendation({action: 'Add', excessReturnPct: 3.21}))
    ).toBe('decision +3.2 pts vs S&P 500');
  });

  it('reads a hold as the name return over the S&P 500', () => {
    expect(
      RecommendationUtils.excessLine(mkRecommendation({action: 'Hold', excessReturnPct: -1.5}))
    ).toBe('decision -1.5 pts vs S&P 500');
  });

  it('reads an exit as the S&P 500 over the name, so the name lagging is a plus', () => {
    expect(
      RecommendationUtils.excessLine(mkRecommendation({action: 'Exit', excessReturnPct: -5.3}))
    ).toBe('decision +5.3 pts vs S&P 500');
  });

  it('reads a trim whose name kept outperforming as a cost', () => {
    expect(
      RecommendationUtils.excessLine(mkRecommendation({action: 'Trim', excessReturnPct: 2}))
    ).toBe('decision -2.0 pts vs S&P 500');
  });

  it('reads par without a sign', () => {
    expect(
      RecommendationUtils.excessLine(mkRecommendation({action: 'Trim', excessReturnPct: 0}))
    ).toBe('decision 0.0 pts vs S&P 500');
  });
});

describe('RecommendationUtils.excessTone', () => {
  it('has no tone until scored', () => {
    expect(RecommendationUtils.excessTone(mkRecommendation())).toBeNull();
  });

  it('has no tone at par', () => {
    expect(RecommendationUtils.excessTone(mkRecommendation({excessReturnPct: 0}))).toBeNull();
  });

  it('is a gain when an add picked a name that beat the S&P 500', () => {
    expect(RecommendationUtils.excessTone(mkRecommendation({action: 'Add', excessReturnPct: 2}))).toBe(
      'gain'
    );
  });

  it('is a loss when an add picked a name that lagged', () => {
    expect(
      RecommendationUtils.excessTone(mkRecommendation({action: 'Add', excessReturnPct: -2}))
    ).toBe('loss');
  });

  it('is a gain when an exit sold a name that then lagged', () => {
    expect(
      RecommendationUtils.excessTone(mkRecommendation({action: 'Exit', excessReturnPct: -2}))
    ).toBe('gain');
  });

  it('is a loss when a trim sold a name that kept beating the S&P 500', () => {
    expect(
      RecommendationUtils.excessTone(mkRecommendation({action: 'Trim', excessReturnPct: 2}))
    ).toBe('loss');
  });
});

describe('RecommendationUtils.orderLines', () => {
  it('splits the order text into its lines', () => {
    expect(RecommendationUtils.orderLines('SELL 660 GRAB / LIMIT 3.13 USD DAY / IBKR')).toEqual([
      'SELL 660 GRAB',
      'LIMIT 3.13 USD DAY',
      'IBKR',
    ]);
  });

  it('drops blank parts', () => {
    expect(RecommendationUtils.orderLines('SELL 1 GRAB /  / IBKR')).toEqual([
      'SELL 1 GRAB',
      'IBKR',
    ]);
  });

  it('is empty with no order text', () => {
    expect(RecommendationUtils.orderLines(null)).toEqual([]);
    expect(RecommendationUtils.orderLines('')).toEqual([]);
  });
});

describe('RecommendationUtils.oldestFirst', () => {
  it('orders by creation, oldest first, without touching the input', () => {
    const newer = mkRecommendation({id: 'b', createdAt: '2026-10-13T09:00:00Z'});
    const older = mkRecommendation({id: 'a', createdAt: '2026-10-01T09:00:00Z'});
    const input = [newer, older];

    expect(RecommendationUtils.oldestFirst(input).map(r => r.id)).toEqual(['a', 'b']);
    expect(input.map(r => r.id)).toEqual(['b', 'a']);
  });
});
