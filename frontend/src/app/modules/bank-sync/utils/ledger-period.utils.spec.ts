import {describe, expect, it} from 'vitest';

import {LedgerPeriodUtils} from './ledger-period.utils';

describe('LedgerPeriodUtils', () => {
  const now = new Date('2026-08-12T10:00:00.000Z');

  describe('dates', () => {
    it('MTD runs from the 1st through today (UTC)', () => {
      expect(LedgerPeriodUtils.dates('mtd', now)).toEqual({from: '2026-08-01', to: '2026-08-12'});
    });

    it('3M is the dashboard 3M window: two months back through this month end', () => {
      expect(LedgerPeriodUtils.dates('3m', now)).toEqual({from: '2026-06-01', to: '2026-08-31'});
    });

    it('YTD starts in January', () => {
      expect(LedgerPeriodUtils.dates('ytd', now)).toEqual({from: '2026-01-01', to: '2026-08-31'});
    });

    it('All is unbounded', () => {
      expect(LedgerPeriodUtils.dates('all', now)).toEqual({from: null, to: null});
    });
  });

  describe('match', () => {
    it('finds the period whose bounds equal the filter', () => {
      expect(LedgerPeriodUtils.match('2026-08-01', '2026-08-12', now)).toBe('mtd');
      expect(LedgerPeriodUtils.match('2026-06-01', '2026-08-31', now)).toBe('3m');
    });

    it('no date filter is All', () => {
      expect(LedgerPeriodUtils.match(null, null, now)).toBe('all');
    });

    it('is null for a custom or partial range', () => {
      expect(LedgerPeriodUtils.match('2026-08-02', '2026-08-12', now)).toBeNull();
      expect(LedgerPeriodUtils.match('2026-08-01', null, now)).toBeNull();
    });
  });
});
