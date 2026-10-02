import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {MonthKeyUtils} from './month-key.utils';

describe('MonthKeyUtils', () => {
  describe('currentUtc', () => {
    beforeEach(() => {
      vi.useFakeTimers();
    });

    afterEach(() => {
      vi.useRealTimers();
    });

    it('returns the UTC year-month with a padded month', () => {
      vi.setSystemTime(new Date('2026-03-05T10:00:00Z'));
      expect(MonthKeyUtils.currentUtc()).toBe('2026-03');
    });

    it('uses the UTC month even when local time has rolled over', () => {
      vi.setSystemTime(new Date('2026-12-31T23:30:00Z'));
      expect(MonthKeyUtils.currentUtc()).toBe('2026-12');
    });
  });

  describe('shift', () => {
    it('moves forward within a year', () => {
      expect(MonthKeyUtils.shift('2026-05', 1)).toBe('2026-06');
    });

    it('moves backward within a year', () => {
      expect(MonthKeyUtils.shift('2026-05', -1)).toBe('2026-04');
    });

    it('rolls the year forward across December', () => {
      expect(MonthKeyUtils.shift('2026-12', 1)).toBe('2027-01');
    });

    it('rolls the year backward across January', () => {
      expect(MonthKeyUtils.shift('2026-01', -1)).toBe('2025-12');
    });

    it('handles multi-month deltas', () => {
      expect(MonthKeyUtils.shift('2026-05', -6)).toBe('2025-11');
    });

    it('returns the same key for a zero delta', () => {
      expect(MonthKeyUtils.shift('2026-05', 0)).toBe('2026-05');
    });
  });

  describe('toDate / fromDate', () => {
    it('toDate returns the first of the month', () => {
      const date = MonthKeyUtils.toDate('2026-09');
      expect([date.getFullYear(), date.getMonth(), date.getDate()]).toEqual([2026, 8, 1]);
    });

    it('fromDate inverts toDate, padding the month', () => {
      expect(MonthKeyUtils.fromDate(new Date(2026, 0, 15))).toBe('2026-01');
      expect(MonthKeyUtils.fromDate(MonthKeyUtils.toDate('2026-12'))).toBe('2026-12');
    });
  });
});
