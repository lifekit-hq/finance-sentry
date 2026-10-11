import {describe, expect, it} from 'vitest';

import {DateUtils} from './date.utils';

// The suite pins TZ=Europe/Dublin (GMT in winter, IST = UTC+1 from the last Sunday of March
// until the last Sunday of October), so a late-evening UTC instant is already tomorrow locally.
describe('DateUtils.isoDateUtc', () => {
  it('reads the UTC calendar day', () => {
    expect(DateUtils.isoDateUtc(new Date('2026-08-12T00:00:00.000Z'))).toBe('2026-08-12');
  });

  it('stays on the UTC day when the local day has already rolled over', () => {
    expect(DateUtils.isoDateUtc(new Date('2026-09-22T23:30:00.000Z'))).toBe('2026-09-22');
  });

  it('zero-pads month and day', () => {
    expect(DateUtils.isoDateUtc(new Date('2026-01-05T12:00:00.000Z'))).toBe('2026-01-05');
  });
});

describe('DateUtils.isoDateLocal', () => {
  it('reads the local calendar date', () => {
    expect(DateUtils.isoDateLocal(new Date(2026, 8, 22, 23, 30))).toBe('2026-09-22');
  });

  it('rolls to the next day when 23:30 UTC is past local midnight (summer, UTC+1)', () => {
    expect(DateUtils.isoDateLocal(new Date('2026-09-22T23:30:00.000Z'))).toBe('2026-09-23');
  });

  it('matches UTC in winter, when Dublin is on UTC+0', () => {
    expect(DateUtils.isoDateLocal(new Date('2026-12-31T23:30:00.000Z'))).toBe('2026-12-31');
  });

  it('flips on the spring-forward night (clocks jump 01:00 → 02:00 on 29 Mar 2026)', () => {
    expect(DateUtils.isoDateLocal(new Date('2026-03-28T23:30:00.000Z'))).toBe('2026-03-28');
    expect(DateUtils.isoDateLocal(new Date('2026-03-29T00:30:00.000Z'))).toBe('2026-03-29');
    expect(DateUtils.isoDateLocal(new Date('2026-03-29T23:30:00.000Z'))).toBe('2026-03-30');
  });

  it('flips on the fall-back night (clocks return on 25 Oct 2026)', () => {
    expect(DateUtils.isoDateLocal(new Date('2026-10-24T22:30:00.000Z'))).toBe('2026-10-24');
    expect(DateUtils.isoDateLocal(new Date('2026-10-24T23:30:00.000Z'))).toBe('2026-10-25');
    expect(DateUtils.isoDateLocal(new Date('2026-10-25T23:30:00.000Z'))).toBe('2026-10-25');
  });
});

describe('DateUtils.monthKeyUtc', () => {
  it('formats yyyy-MM with a padded month', () => {
    expect(DateUtils.monthKeyUtc(new Date('2026-03-15T12:00:00.000Z'))).toBe('2026-03');
  });

  it('stays in the UTC month on the last evening of a month', () => {
    expect(DateUtils.monthKeyUtc(new Date('2026-04-30T23:30:00.000Z'))).toBe('2026-04');
  });

  it('rolls the year only at UTC midnight', () => {
    expect(DateUtils.monthKeyUtc(new Date('2026-12-31T23:59:59.000Z'))).toBe('2026-12');
    expect(DateUtils.monthKeyUtc(new Date('2027-01-01T00:00:00.000Z'))).toBe('2027-01');
  });
});

describe('DateUtils.monthKeyLocal', () => {
  it('reads the local month', () => {
    expect(DateUtils.monthKeyLocal(new Date(2026, 8, 1))).toBe('2026-09');
  });

  it('is already next month when the last UTC evening is past local midnight', () => {
    expect(DateUtils.monthKeyLocal(new Date('2026-04-30T23:30:00.000Z'))).toBe('2026-05');
  });
});

describe('DateUtils.dayLabel', () => {
  it('formats weekday, day and month in en-GB order', () => {
    expect(DateUtils.dayLabel('2026-10-05')).toBe('Mon 5 Oct');
  });

  it('adds the year on request', () => {
    expect(DateUtils.dayLabel('2025-12-29', true)).toBe('Mon 29 Dec 2025');
  });

  it('does not shift a calendar day around a DST change', () => {
    expect(DateUtils.dayLabel('2026-03-29')).toBe('Sun 29 Mar');
    expect(DateUtils.dayLabel('2026-10-25')).toBe('Sun 25 Oct');
  });
});
