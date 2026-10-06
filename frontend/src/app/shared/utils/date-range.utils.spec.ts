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

  it('holds 1m to the previous month last day instead of overflowing', () => {
    expect(DateRangeUtils.fromRelativeRange('1m', new Date('2026-03-31T00:00:00.000Z')).from).toBe(
      '2026-02-28'
    );
  });

  it('starts 1w six days back so the window is seven days including today', () => {
    expect(DateRangeUtils.fromRelativeRange('1w', now)).toEqual({
      from: '2026-08-06',
      to: '2026-08-12',
    });
  });

  it('starts mtd on the first of the month', () => {
    expect(DateRangeUtils.fromRelativeRange('mtd', now)).toEqual({
      from: '2026-08-01',
      to: '2026-08-12',
    });
  });

  it('starts 1w in the previous year across New Year', () => {
    expect(DateRangeUtils.fromRelativeRange('1w', new Date('2027-01-03T00:00:00.000Z')).from).toBe(
      '2026-12-28'
    );
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
