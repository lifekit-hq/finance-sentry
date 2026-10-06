import {describe, expect, it} from 'vitest';

import {DashboardRangeUtils} from './dashboard-range.utils';

describe('DashboardRangeUtils', () => {
  const now = new Date('2026-08-12T00:00:00.000Z');
  const january = new Date('2026-01-03T00:00:00.000Z');

  describe('windowMonths', () => {
    it('counts the in-progress month in every fixed range', () => {
      expect(DashboardRangeUtils.windowMonths('1m', now)).toBe(1);
      expect(DashboardRangeUtils.windowMonths('3m', now)).toBe(3);
      expect(DashboardRangeUtils.windowMonths('1y', now)).toBe(12);
    });

    it('counts January through the current month for year-to-date', () => {
      expect(DashboardRangeUtils.windowMonths('ytd', now)).toBe(8);
      expect(DashboardRangeUtils.windowMonths('ytd', january)).toBe(1);
    });
  });

  describe('months', () => {
    it('requests complete months only, since the backend adds the in-progress one', () => {
      expect(DashboardRangeUtils.months('3m', now)).toBe(2);
      expect(DashboardRangeUtils.months('6m', now)).toBe(5);
      expect(DashboardRangeUtils.months('1y', now)).toBe(11);
      expect(DashboardRangeUtils.months('ytd', now)).toBe(7);
    });

    it('stays at the backend minimum of one for the current-month windows', () => {
      expect(DashboardRangeUtils.months('1m', now)).toBe(1);
      expect(DashboardRangeUtils.months('ytd', january)).toBe(1);
    });

    it('stays within the backend maximum for all', () => {
      expect(DashboardRangeUtils.months('all', now)).toBe(119);
    });
  });

  describe('windowStartKey', () => {
    it('starts one-month and January year-to-date windows at the current month', () => {
      expect(DashboardRangeUtils.windowStartKey('1m', now)).toBe('2026-08');
      expect(DashboardRangeUtils.windowStartKey('ytd', january)).toBe('2026-01');
    });

    it('starts year-to-date at January', () => {
      expect(DashboardRangeUtils.windowStartKey('ytd', now)).toBe('2026-01');
    });

    it('steps back across a year boundary', () => {
      expect(DashboardRangeUtils.windowStartKey('3m', now)).toBe('2026-06');
      expect(DashboardRangeUtils.windowStartKey('1y', now)).toBe('2025-09');
      expect(DashboardRangeUtils.windowStartKey('6m', january)).toBe('2025-08');
    });
  });
});
