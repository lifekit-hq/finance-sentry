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
    it('requests N complete months of chart history for the fixed ranges', () => {
      expect(DashboardRangeUtils.months('1m', now)).toBe(1);
      expect(DashboardRangeUtils.months('3m', now)).toBe(3);
      expect(DashboardRangeUtils.months('6m', now)).toBe(6);
      expect(DashboardRangeUtils.months('1y', now)).toBe(12);
    });

    it('requests the closed months since January for year-to-date', () => {
      expect(DashboardRangeUtils.months('ytd', now)).toBe(7);
    });

    it('stays at the backend minimum of one for January year-to-date', () => {
      expect(DashboardRangeUtils.months('ytd', january)).toBe(1);
    });

    it('requests the backend maximum for all', () => {
      expect(DashboardRangeUtils.months('all', now)).toBe(120);
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

  describe('windowDates', () => {
    it('spans the first day of the window start month through the last day of this month', () => {
      expect(DashboardRangeUtils.windowDates('3m', now)).toEqual({
        from: '2026-06-01',
        to: '2026-08-31',
      });
      expect(DashboardRangeUtils.windowDates('1m', now)).toEqual({
        from: '2026-08-01',
        to: '2026-08-31',
      });
    });

    it('starts year-to-date in January and handles leap-year month ends', () => {
      expect(DashboardRangeUtils.windowDates('ytd', now).from).toBe('2026-01-01');
      expect(DashboardRangeUtils.windowDates('1m', new Date('2028-02-10T00:00:00.000Z')).to).toBe(
        '2028-02-29'
      );
    });

    it('is unbounded for all', () => {
      expect(DashboardRangeUtils.windowDates('all', now)).toEqual({});
    });
  });
});
