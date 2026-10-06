import {describe, expect, it} from 'vitest';

import {type HistoryRange} from '../models/dashboard/dashboard.model';
import {DashboardRangeUtils} from './dashboard-range.utils';

describe('DashboardRangeUtils', () => {
  const now = new Date('2026-08-12T00:00:00.000Z');
  const january = new Date('2026-01-03T00:00:00.000Z');

  describe('isDayWindow', () => {
    it('is true for the day-resolution presets only', () => {
      expect(
        ['1w', 'mtd', '1m'].map(r => DashboardRangeUtils.isDayWindow(r as HistoryRange))
      ).toEqual([true, true, true]);
      expect(
        ['3m', 'ytd', '1y', 'all'].some(r => DashboardRangeUtils.isDayWindow(r as HistoryRange))
      ).toBe(false);
    });
  });

  describe('windowFrom', () => {
    it('starts 1W six days back, so today is the seventh day', () => {
      expect(DashboardRangeUtils.windowFrom('1w', now)).toBe('2026-08-06');
    });

    it('starts 1W in the previous month when the week straddles the boundary', () => {
      expect(DashboardRangeUtils.windowFrom('1w', new Date('2026-10-01T09:00:00.000Z'))).toBe(
        '2026-09-25'
      );
    });

    it('starts 1W in the previous year when the week straddles New Year', () => {
      expect(DashboardRangeUtils.windowFrom('1w', new Date('2027-01-03T09:00:00.000Z'))).toBe(
        '2026-12-28'
      );
    });

    it('starts MTD on the first of the month, including on the 1st itself', () => {
      expect(DashboardRangeUtils.windowFrom('mtd', now)).toBe('2026-08-01');
      expect(DashboardRangeUtils.windowFrom('mtd', new Date('2026-10-01T00:00:00.000Z'))).toBe(
        '2026-10-01'
      );
      expect(DashboardRangeUtils.windowFrom('mtd', new Date('2026-12-31T23:59:59.000Z'))).toBe(
        '2026-12-01'
      );
    });

    it('starts 1M on the same day a month ago and holds it to the shorter month', () => {
      expect(DashboardRangeUtils.windowFrom('1m', now)).toBe('2026-07-12');
      expect(DashboardRangeUtils.windowFrom('1m', new Date('2026-03-31T00:00:00.000Z'))).toBe(
        '2026-02-28'
      );
      expect(DashboardRangeUtils.windowFrom('1m', new Date('2028-03-31T00:00:00.000Z'))).toBe(
        '2028-02-29'
      );
      expect(DashboardRangeUtils.windowFrom('1m', new Date('2027-01-15T00:00:00.000Z'))).toBe(
        '2026-12-15'
      );
    });

    it('anchors in UTC, not the local zone', () => {
      // 23:30 UTC on the 31st is already the 1st in Europe/Dublin in summer; UTC still says the 31st.
      expect(DashboardRangeUtils.windowFrom('mtd', new Date('2026-07-31T23:30:00.000Z'))).toBe(
        '2026-07-01'
      );
    });

    it('is undefined for month-based ranges', () => {
      expect(DashboardRangeUtils.windowFrom('3m', now)).toBeUndefined();
      expect(DashboardRangeUtils.windowFrom('ytd', now)).toBeUndefined();
      expect(DashboardRangeUtils.windowFrom('all', now)).toBeUndefined();
    });
  });

  describe('windowMonths', () => {
    it('spans the start month through the current one for day ranges', () => {
      expect(DashboardRangeUtils.windowMonths('mtd', now)).toBe(1);
      expect(DashboardRangeUtils.windowMonths('1w', now)).toBe(1);
      expect(DashboardRangeUtils.windowMonths('1w', new Date('2026-10-01T09:00:00.000Z'))).toBe(2);
      expect(DashboardRangeUtils.windowMonths('1m', now)).toBe(2);
      expect(DashboardRangeUtils.windowMonths('1w', new Date('2027-01-03T09:00:00.000Z'))).toBe(2);
    });

    it('counts the in-progress month in every fixed range', () => {
      expect(DashboardRangeUtils.windowMonths('3m', now)).toBe(3);
      expect(DashboardRangeUtils.windowMonths('1y', now)).toBe(12);
    });

    it('counts January through the current month for year-to-date', () => {
      expect(DashboardRangeUtils.windowMonths('ytd', now)).toBe(8);
      expect(DashboardRangeUtils.windowMonths('ytd', january)).toBe(1);
    });
  });

  describe('months', () => {
    it('loads the previous month for day ranges, which covers each of their start days', () => {
      expect(DashboardRangeUtils.months('1w', now)).toBe(1);
      expect(DashboardRangeUtils.months('mtd', now)).toBe(1);
      expect(DashboardRangeUtils.months('1m', now)).toBe(1);
    });

    it('requests N complete months of chart history for the fixed ranges', () => {
      expect(DashboardRangeUtils.months('3m', now)).toBe(3);
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
    it('starts MTD and January year-to-date windows at the current month', () => {
      expect(DashboardRangeUtils.windowStartKey('mtd', now)).toBe('2026-08');
      expect(DashboardRangeUtils.windowStartKey('ytd', january)).toBe('2026-01');
    });

    it('starts year-to-date at January', () => {
      expect(DashboardRangeUtils.windowStartKey('ytd', now)).toBe('2026-01');
    });

    it('steps back across a year boundary', () => {
      expect(DashboardRangeUtils.windowStartKey('3m', now)).toBe('2026-06');
      expect(DashboardRangeUtils.windowStartKey('1y', now)).toBe('2025-09');
      expect(DashboardRangeUtils.windowStartKey('1y', january)).toBe('2025-02');
    });
  });

  describe('windowDates', () => {
    it('spans the first day of the window start month through the last day of this month', () => {
      expect(DashboardRangeUtils.windowDates('3m', now)).toEqual({
        from: '2026-06-01',
        to: '2026-08-31',
      });
    });

    it('spans the start day through today for day-resolution ranges', () => {
      expect(DashboardRangeUtils.windowDates('1w', now)).toEqual({
        from: '2026-08-06',
        to: '2026-08-12',
      });
      expect(DashboardRangeUtils.windowDates('mtd', now)).toEqual({
        from: '2026-08-01',
        to: '2026-08-12',
      });
      expect(DashboardRangeUtils.windowDates('1m', now)).toEqual({
        from: '2026-07-12',
        to: '2026-08-12',
      });
      expect(DashboardRangeUtils.windowDates('mtd', new Date('2026-10-01T00:00:00.000Z'))).toEqual({
        from: '2026-10-01',
        to: '2026-10-01',
      });
    });

    it('starts year-to-date in January and handles leap-year month ends', () => {
      expect(DashboardRangeUtils.windowDates('ytd', now).from).toBe('2026-01-01');
      expect(DashboardRangeUtils.windowDates('ytd', new Date('2028-02-10T00:00:00.000Z')).to).toBe(
        '2028-02-29'
      );
    });

    it('is unbounded for all', () => {
      expect(DashboardRangeUtils.windowDates('all', now)).toEqual({});
    });
  });

  describe('breakdownParams', () => {
    it('carries a day window from its start day through today, with the dashboard months', () => {
      expect(DashboardRangeUtils.breakdownParams('1w', now)).toEqual({
        from: '2026-08-06',
        to: '2026-08-12',
        months: 1,
      });
      expect(DashboardRangeUtils.breakdownParams('mtd', now)).toEqual({
        from: '2026-08-01',
        to: '2026-08-12',
        months: 1,
      });
    });

    it('carries a month-based window from its first day, stopping at today', () => {
      expect(DashboardRangeUtils.breakdownParams('3m', now)).toEqual({
        from: '2026-06-01',
        to: '2026-08-12',
        months: 3,
      });
      expect(DashboardRangeUtils.breakdownParams('ytd', now)).toEqual({
        from: '2026-01-01',
        to: '2026-08-12',
        months: 7,
      });
    });

    it('leaves all-time without a lower bound', () => {
      expect(DashboardRangeUtils.breakdownParams('all', now)).toEqual({
        to: '2026-08-12',
        months: 120,
      });
    });
  });
});
