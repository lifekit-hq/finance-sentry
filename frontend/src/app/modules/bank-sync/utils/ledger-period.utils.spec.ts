import {describe, expect, it} from 'vitest';

import {LedgerPeriodUtils} from './ledger-period.utils';

describe('LedgerPeriodUtils', () => {
  const now = new Date('2026-08-12T10:00:00.000Z');

  describe('dates', () => {
    it('this month runs from the 1st through today (UTC)', () => {
      expect(LedgerPeriodUtils.dates('this-month', now)).toEqual({
        from: '2026-08-01',
        to: '2026-08-12',
      });
    });

    it('this month on the 1st is a single day', () => {
      expect(LedgerPeriodUtils.dates('this-month', new Date('2026-10-01T00:00:00.000Z'))).toEqual({
        from: '2026-10-01',
        to: '2026-10-01',
      });
    });

    it('anchors in UTC, not the viewer zone, at a month boundary', () => {
      // 23:30 on Aug 31 in UTC-5 is already Sep 1 in UTC.
      expect(LedgerPeriodUtils.dates('this-month', new Date('2026-09-01T04:30:00.000Z'))).toEqual({
        from: '2026-09-01',
        to: '2026-09-01',
      });
    });

    it('last month is the whole previous calendar month', () => {
      expect(LedgerPeriodUtils.dates('last-month', now)).toEqual({
        from: '2026-07-01',
        to: '2026-07-31',
      });
    });

    it('last month ends on Feb 28 / 29 correctly', () => {
      expect(LedgerPeriodUtils.dates('last-month', new Date('2026-03-31T00:00:00.000Z'))).toEqual({
        from: '2026-02-01',
        to: '2026-02-28',
      });
      expect(LedgerPeriodUtils.dates('last-month', new Date('2028-03-01T00:00:00.000Z'))).toEqual({
        from: '2028-02-01',
        to: '2028-02-29',
      });
    });

    it('last month in January is December of the previous year', () => {
      expect(LedgerPeriodUtils.dates('last-month', new Date('2027-01-03T00:00:00.000Z'))).toEqual({
        from: '2026-12-01',
        to: '2026-12-31',
      });
    });

    it('3M is the dashboard 3M window: two months back through this month end', () => {
      expect(LedgerPeriodUtils.dates('3m', now)).toEqual({from: '2026-06-01', to: '2026-08-31'});
    });

    it('3M spans the year boundary', () => {
      expect(LedgerPeriodUtils.dates('3m', new Date('2027-01-15T00:00:00.000Z'))).toEqual({
        from: '2026-11-01',
        to: '2027-01-31',
      });
    });
  });

  describe('match', () => {
    it('finds the period whose bounds equal the filter', () => {
      expect(LedgerPeriodUtils.match('2026-08-01', '2026-08-12', now)).toBe('this-month');
      expect(LedgerPeriodUtils.match('2026-07-01', '2026-07-31', now)).toBe('last-month');
      expect(LedgerPeriodUtils.match('2026-06-01', '2026-08-31', now)).toBe('3m');
    });

    it('is null for a custom or partial range and for no range', () => {
      expect(LedgerPeriodUtils.match('2026-08-02', '2026-08-12', now)).toBeNull();
      expect(LedgerPeriodUtils.match('2026-08-01', null, now)).toBeNull();
      expect(LedgerPeriodUtils.match(null, null, now)).toBeNull();
    });
  });
});
