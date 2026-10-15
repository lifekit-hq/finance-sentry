import {describe, expect, it} from 'vitest';

import {type Headline} from '../models/headline/headline.model';
import {HeadlineUtils} from './headline.utils';

function headline(overrides: Partial<Headline> = {}): Headline {
  return {
    amount: 100,
    percent: null,
    method: 'VALUE',
    scope: 'umbrella',
    window: {to: '2026-10-15'},
    quality: 'COMPLETE',
    reasons: [],
    currency: 'USD',
    ...overrides,
  };
}

describe('HeadlineUtils.badge', () => {
  it('is null for a complete figure', () => {
    expect(HeadlineUtils.badge(headline())).toBeNull();
  });

  it('is null when there is no headline (an older API build)', () => {
    expect(HeadlineUtils.badge(null)).toBeNull();
    expect(HeadlineUtils.badge(undefined)).toBeNull();
  });

  it('reads Estimated for an estimated figure', () => {
    expect(
      HeadlineUtils.badge(headline({quality: 'ESTIMATED', reasons: [{code: 'FX_TODAY_RATE'}]}))
    ).toEqual({kind: 'estimated', label: 'Estimated'});
  });

  it('reads Stale for a degraded figure with a stale source', () => {
    expect(
      HeadlineUtils.badge(
        headline({quality: 'DEGRADED', reasons: [{code: 'STALE_SOURCE', source: 'banking'}]})
      )
    ).toEqual({kind: 'stale', label: 'Stale'});
  });

  it('reads Partial for a degraded figure with no stale source', () => {
    expect(
      HeadlineUtils.badge(headline({quality: 'DEGRADED', reasons: [{code: 'FX_UNKNOWN_1TO1'}]}))
    ).toEqual({kind: 'partial', label: 'Partial'});
  });

  it('reads Unavailable for a figure with no amount', () => {
    expect(
      HeadlineUtils.badge(
        headline({amount: null, quality: 'UNAVAILABLE', reasons: [{code: 'NO_SPLIT'}]})
      )
    ).toEqual({kind: 'unavailable', label: 'Unavailable'});
  });
});

describe('HeadlineUtils.display', () => {
  it('shows N/A, never the formatted zero, for an unavailable figure', () => {
    expect(HeadlineUtils.display(headline({amount: null, quality: 'UNAVAILABLE'}), '$0.00')).toBe(
      'N/A'
    );
  });

  it('keeps the formatted figure otherwise, and when there is no headline', () => {
    expect(HeadlineUtils.display(headline({quality: 'DEGRADED'}), '$5.00')).toBe('$5.00');
    expect(HeadlineUtils.display(null, '$5.00')).toBe('$5.00');
  });
});

describe('HeadlineUtils.combine', () => {
  it('is null when no part is known', () => {
    expect(HeadlineUtils.combine([null, undefined])).toBeNull();
  });

  it('sums the amounts and takes the worst quality, the union of reasons and the oldest asOf', () => {
    const combined = HeadlineUtils.combine([
      headline({amount: 100, asOf: '2026-10-15T10:00:00Z'}),
      headline({
        amount: 50,
        quality: 'DEGRADED',
        reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
        asOf: '2026-10-14T08:00:00Z',
        coverage: 0.8,
      }),
    ]);
    expect(combined).toMatchObject({
      amount: 150,
      quality: 'DEGRADED',
      reasons: [{code: 'STALE_SOURCE', source: 'crypto'}],
      asOf: '2026-10-14T08:00:00Z',
      coverage: 0.8,
    });
  });

  it('de-duplicates identical reasons', () => {
    const reason = {code: 'FX_TODAY_RATE'};
    const combined = HeadlineUtils.combine([
      headline({quality: 'ESTIMATED', reasons: [reason]}),
      headline({quality: 'ESTIMATED', reasons: [reason]}),
    ]);
    expect(combined?.reasons).toEqual([reason]);
  });

  it('leaves an unavailable part out of the sum and reads Partial, not N/A', () => {
    const combined = HeadlineUtils.combine([
      headline({amount: 100}),
      headline({amount: null, quality: 'UNAVAILABLE', reasons: [{code: 'NO_SPLIT'}]}),
    ]);
    expect(combined).toMatchObject({amount: 100, quality: 'DEGRADED'});
    expect(HeadlineUtils.badge(combined)?.label).toBe('Partial');
  });

  it('is unavailable, with no amount, only when every part is', () => {
    const unavailable = headline({
      amount: null,
      quality: 'UNAVAILABLE',
      reasons: [{code: 'NO_SPLIT'}],
    });
    expect(HeadlineUtils.combine([unavailable, unavailable])).toMatchObject({
      amount: null,
      quality: 'UNAVAILABLE',
    });
  });

  it('keeps the percent of a single part only', () => {
    expect(HeadlineUtils.combine([headline({percent: 4})])?.percent).toBe(4);
    expect(
      HeadlineUtils.combine([headline({percent: 4}), headline({percent: 6})])?.percent
    ).toBeNull();
  });
});

describe('HeadlineUtils.withStaleSources', () => {
  it('returns the headline itself when nothing failed', () => {
    const h = headline();
    expect(HeadlineUtils.withStaleSources(h, [], 'USD')).toBe(h);
    expect(HeadlineUtils.withStaleSources(null, [], 'USD')).toBeNull();
  });

  it('adds a STALE_SOURCE reason per failed source and degrades the figure', () => {
    const result = HeadlineUtils.withStaleSources(headline(), ['crypto'], 'USD');
    expect(result?.quality).toBe('DEGRADED');
    expect(result?.reasons).toEqual([{code: 'STALE_SOURCE', source: 'crypto'}]);
    expect(HeadlineUtils.badge(result)?.label).toBe('Stale');
  });

  it('keeps an already-unavailable figure unavailable', () => {
    const result = HeadlineUtils.withStaleSources(
      headline({amount: null, quality: 'UNAVAILABLE'}),
      ['crypto'],
      'USD'
    );
    expect(result?.quality).toBe('UNAVAILABLE');
  });

  it('builds an unavailable headline when there was none to extend', () => {
    const result = HeadlineUtils.withStaleSources(null, ['brokerage', 'crypto'], 'USD');
    expect(result).toMatchObject({amount: null, quality: 'UNAVAILABLE', currency: 'USD'});
    expect(result?.reasons).toHaveLength(2);
  });

  it('builds a degraded headline around a known amount when there was none to extend', () => {
    const result = HeadlineUtils.withStaleSources(null, ['banking'], 'USD', 250);
    expect(result).toMatchObject({amount: 250, quality: 'DEGRADED'});
    expect(HeadlineUtils.badge(result)?.label).toBe('Stale');
  });
});

describe('HeadlineUtils labels', () => {
  it('names a provider by its display name and capitalises any other source', () => {
    expect(HeadlineUtils.sourceLabel('banking')).toBe('Banking');
    expect(HeadlineUtils.sourceLabel('binance')).not.toBe('binance');
  });

  it('formats a coverage fraction as a whole percent', () => {
    expect(HeadlineUtils.coverageLabel(0.8)).toBe('80%');
    expect(HeadlineUtils.coverageLabel(0.666)).toBe('67%');
  });
});
