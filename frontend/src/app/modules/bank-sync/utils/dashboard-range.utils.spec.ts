import {describe, expect, it} from 'vitest';

import {DashboardRangeUtils} from './dashboard-range.utils';

describe('DashboardRangeUtils.months', () => {
  const now = new Date('2026-08-12T00:00:00.000Z');

  it('maps fixed ranges to their month count', () => {
    expect(DashboardRangeUtils.months('1m', now)).toBe(1);
    expect(DashboardRangeUtils.months('3m', now)).toBe(3);
    expect(DashboardRangeUtils.months('1y', now)).toBe(12);
  });

  it('counts January through the current month for year-to-date', () => {
    expect(DashboardRangeUtils.months('ytd', now)).toBe(8);
    expect(DashboardRangeUtils.months('ytd', new Date('2026-01-03T00:00:00.000Z'))).toBe(1);
  });

  it('uses the backend maximum window for all', () => {
    expect(DashboardRangeUtils.months('all', now)).toBe(120);
  });
});
