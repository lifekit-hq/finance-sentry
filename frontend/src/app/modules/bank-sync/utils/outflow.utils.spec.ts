import {describe, expect, it} from 'vitest';

import {type MonthlyFlow} from '../models/dashboard/dashboard.model';
import {OutflowUtils} from './outflow.utils';

function flow(month: string, outflowUsd: number): MonthlyFlow {
  return {
    month,
    currency: 'USD',
    inflow: 0,
    outflow: outflowUsd,
    net: -outflowUsd,
    inflowUsd: 0,
    outflowUsd,
    netUsd: -outflowUsd,
  };
}

const HISTORY = [
  flow('2026-07', 100),
  flow('2026-08', 200),
  flow('2026-09', 300),
  flow('2026-10', 400),
];

describe('OutflowUtils', () => {
  describe('sumMonths', () => {
    it('sums one month for a single-month range', () => {
      expect(OutflowUtils.sumMonths(HISTORY, '2026-10-01', '2026-10-31')).toBe(400);
    });

    it('sums the months a multi-month range touches', () => {
      expect(OutflowUtils.sumMonths(HISTORY, '2026-08-01', '2026-10-31')).toBe(900);
    });

    it('sums the whole history when the range is open', () => {
      expect(OutflowUtils.sumMonths(HISTORY, null, null)).toBe(1000);
    });

    it('is zero when no month falls in the range', () => {
      expect(OutflowUtils.sumMonths(HISTORY, '2025-01-01', '2025-01-31')).toBe(0);
    });
  });

  describe('sameScope', () => {
    const scope = {accountId: 'a', from: '2026-10-01', to: '2026-10-31'};

    it('matches an identical scope', () => {
      expect(OutflowUtils.sameScope(scope, {...scope})).toBe(true);
    });

    it.each([{accountId: 'b'}, {from: null}, {to: '2026-10-30'}])('differs on %o', change => {
      expect(OutflowUtils.sameScope(scope, {...scope, ...change})).toBe(false);
    });
  });
});
