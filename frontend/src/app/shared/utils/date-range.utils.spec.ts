import {describe, expect, it} from 'vitest';

import {DateRangeUtils} from './date-range.utils';

describe('DateRangeUtils.fromRelativeRange', () => {
  const now = new Date('2026-08-12T00:00:00.000Z');

  it('reaches one month back for 1m', () => {
    expect(DateRangeUtils.fromRelativeRange('1m', now)).toEqual({
      from: '2026-07-12',
      to: '2026-08-12',
    });
  });

  it('starts at January 1st for ytd', () => {
    expect(DateRangeUtils.fromRelativeRange('ytd', now)).toEqual({
      from: '2026-01-01',
      to: '2026-08-12',
    });
  });

  it('leaves the start open for all', () => {
    expect(DateRangeUtils.fromRelativeRange('all', now)).toEqual({from: null, to: '2026-08-12'});
  });
});
