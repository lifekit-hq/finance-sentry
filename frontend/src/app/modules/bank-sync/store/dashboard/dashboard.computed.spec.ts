import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type DashboardData,
  type MonthlyFlow,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {dashboardComputed} from './dashboard.computed';

// Frozen mid-month so "the current month" is genuinely partial: 12 of 31 days elapsed.
// Every pace assertion below is anchored to that 12/31 fraction.
const NOW = new Date('2026-08-12T00:00:00.000Z');
const ELAPSED_FRACTION = 12 / 31;

function flow(month: string, inflow: number, outflow: number): MonthlyFlow {
  return {
    month,
    currency: 'USD',
    inflow,
    outflow,
    net: inflow - outflow,
    inflowUsd: inflow,
    outflowUsd: outflow,
    netUsd: inflow - outflow,
  };
}

interface Fixture {
  monthlyFlow: MonthlyFlow[];
  totalNetWorthUsd?: number;
  netWorthHistory?: NetWorthSnapshotDto[];
}

function build({monthlyFlow, totalNetWorthUsd = 0, netWorthHistory = []}: Fixture) {
  const data: DashboardData = {
    aggregatedBalance: {USD: 0},
    totalNetWorthUsd,
    accountCount: 1,
    accountsByType: {},
    monthlyFlow,
    topCategories: [],
    lastSyncTimestamp: null,
  } as unknown as DashboardData;

  return {
    data: signal<Nullable<DashboardData>>(data),
    netWorthHistory: signal<NetWorthSnapshotDto[]>(netWorthHistory),
    historyLoading: signal(false),
    historyError: signal<string | null>(null),
  };
}

function computedFor(monthlyFlow: MonthlyFlow[]) {
  return TestBed.runInInjectionContext(() => dashboardComputed(build({monthlyFlow})));
}

function projectionFor(fixture: Fixture) {
  return TestBed.runInInjectionContext(() => dashboardComputed(build(fixture)));
}

function snapshot(banking: number, brokerage: number, crypto: number): NetWorthSnapshotDto {
  return {
    snapshotDate: '2026-07-31',
    bankingTotal: banking,
    brokerageTotal: brokerage,
    cryptoTotal: crypto,
    totalNetWorth: banking + brokerage + crypto,
    currency: 'USD',
  };
}

/**
 * Three closed months netting −200, +1000 and +5000, plus a partial August. The +5000 stands
 * in for the June 2026 duplicate-salary artifact: median 1000, mean 1933.
 */
const SKEWED_MONTHS: MonthlyFlow[] = [
  flow('2026-05', 1000, 1200),
  flow('2026-06', 5000, 4000),
  flow('2026-07', 6000, 1000),
  flow('2026-08', 400, 300),
];

/** Three closed months at a steady 4000 in / 2000 out, plus a partial August. */
function steadyHistory(augustInflow: number, augustOutflow: number): MonthlyFlow[] {
  return [
    flow('2026-05', 4000, 2000),
    flow('2026-06', 4000, 2000),
    flow('2026-07', 4000, 2000),
    flow('2026-08', augustInflow, augustOutflow),
  ];
}

describe('dashboardComputed', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideApiBaseUrl('http://localhost/api/v1'),
      ],
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  describe('the chart plots complete calendar months only', () => {
    it('keeps the in-progress month out of income vs spending', () => {
      const bars = computedFor(steadyHistory(500, 900)).incomeVsSpendingBars();

      expect(bars.map(s => s.label)).toEqual(['Income', 'Spending']);
      for (const series of bars) {
        expect(series.points.map(p => p.label)).toEqual(["May '26", "Jun '26", "Jul '26"]);
      }
    });

    it('reports no chartable data when only the in-progress month exists', () => {
      const c = computedFor([flow('2026-08', 500, 900)]);

      expect(c.hasCashFlow()).toBe(false);
      expect(c.incomeVsSpendingBars()).toEqual([]);
    });
  });

  describe('month-to-date tiles carry the in-progress month', () => {
    it('reports month-to-date income and spending, not the closed months', () => {
      const c = computedFor(steadyHistory(500, 900));

      expect(c.monthlyInflowFormatted()).toBe('$500.00');
      expect(c.monthlySpendingFormatted()).toBe('$900.00');
    });

    it('keeps cents on the month-to-date tiles', () => {
      const c = computedFor(steadyHistory(500, 169.42));

      expect(c.monthlySpendingFormatted()).toBe('$169.42');
    });

    it('falls back to an em dash when the current month has no rows yet', () => {
      const c = computedFor([flow('2026-07', 4000, 2000)]);

      expect(c.monthlyInflowFormatted()).toBe('—');
      expect(c.monthlySpendingFormatted()).toBe('—');
    });

    it('paces spending against the prorated average of the closed months', () => {
      // Exactly on pace: 2000 * 12/31 ≈ 774.
      const onPace = computedFor(steadyHistory(4000, 2000 * ELAPSED_FRACTION));

      expect(onPace.spendingPaceLabel()).toBe('0% over pace');
    });

    it('colours overspending red by inverting the delta the card reads', () => {
      const c = computedFor(steadyHistory(4000, 2000 * ELAPSED_FRACTION * 1.5));

      expect(c.spendingPaceLabel()).toBe('50% over pace');
      // The card renders delta >= 0 as green; spending above pace must not be green.
      expect(c.spendingPaceDelta()).toBeLessThan(0);
    });

    it('colours underspending green', () => {
      const c = computedFor(steadyHistory(4000, 2000 * ELAPSED_FRACTION * 0.5));

      expect(c.spendingPaceLabel()).toBe('50% under pace');
      expect(c.spendingPaceDelta()).toBeGreaterThan(0);
    });

    it('colours income above pace green and below pace red', () => {
      const ahead = computedFor(steadyHistory(4000 * ELAPSED_FRACTION * 1.2, 0));
      expect(ahead.inflowPaceLabel()).toBe('20% above pace');
      expect(ahead.inflowPaceDelta()).toBeGreaterThan(0);

      const behind = computedFor(steadyHistory(4000 * ELAPSED_FRACTION * 0.7, 0));
      expect(behind.inflowPaceLabel()).toBe('30% below pace');
      expect(behind.inflowPaceDelta()).toBeLessThan(0);
    });

    it('reads a month with no income yet as neutral rather than a red shortfall', () => {
      const c = computedFor(steadyHistory(0, 300));

      expect(c.inflowPaceLabel()).toBe('No income yet this month');
      expect(c.inflowPaceDelta()).toBe(0);
    });

    it('shows no pace chip when there are no closed months to compare against', () => {
      const c = computedFor([flow('2026-08', 500, 900)]);

      expect(c.inflowPaceDelta()).toBeNull();
      expect(c.spendingPaceDelta()).toBeNull();
      expect(c.inflowPaceLabel()).toBe('');
    });
  });

  describe('month-to-date savings rate waits for income to land', () => {
    it('withholds the rate while this month is mostly spending against stray credits', () => {
      // $200 against a normal $4000 month — salary has not posted, so the raw rate would
      // read about -350% and mean nothing.
      const c = computedFor(steadyHistory(200, 900));

      expect(c.savingsRateMonthToDateFormatted()).toBe('—');
      expect(c.savingsRatePaceDelta()).toBeNull();
    });

    it('reports the rate once income has substantially landed', () => {
      const c = computedFor(steadyHistory(4000, 1000));

      expect(c.savingsRateMonthToDateFormatted()).toBe('75%');
    });

    it('compares the rate in percentage points against the usual closed months', () => {
      // Closed months run at 50%; this month is at 75%.
      const c = computedFor(steadyHistory(4000, 1000));

      expect(c.savingsRatePaceLabel()).toBe('25 pts above usual');
      expect(c.savingsRatePaceDelta()).toBeGreaterThan(0);
    });

    it('flags a rate below the usual months as negative', () => {
      const c = computedFor(steadyHistory(4000, 3000));

      expect(c.savingsRatePaceLabel()).toBe('25 pts below usual');
      expect(c.savingsRatePaceDelta()).toBeLessThan(0);
    });

    it('withholds the rate when the current month has no rows at all', () => {
      const c = computedFor([flow('2026-07', 4000, 2000)]);

      expect(c.savingsRateMonthToDateFormatted()).toBe('—');
    });
  });

  describe('twelve-month projection from savings contributions', () => {
    it('projects from the MEDIAN month, so one artifact month cannot drag the number', () => {
      // Nets are −200 / +1000 / +5000. Median 1000 → +12,000 over the horizon.
      // A mean (1933) would have produced $33,200 off the same months.
      const c = projectionFor({monthlyFlow: SKEWED_MONTHS, totalNetWorthUsd: 10_000});

      expect(c.projectedNetWorthFormatted()).toBe('$22,000');
    });

    it('averages the two middle months when the sample is even', () => {
      const c = projectionFor({
        monthlyFlow: [
          flow('2026-04', 1000, 900), // +100
          flow('2026-05', 1000, 700), // +300
          flow('2026-06', 1000, 500), // +500
          flow('2026-07', 1000, 100), // +900
        ],
        totalNetWorthUsd: 0,
      });

      // Middle two are 300 and 500 → 400/mo → 4,800 over twelve months.
      expect(c.projectedNetWorthFormatted()).toBe('$4,800');
    });

    it('renders nothing below three complete months', () => {
      const c = projectionFor({
        monthlyFlow: [
          flow('2026-06', 4000, 2000),
          flow('2026-07', 4000, 2000),
          flow('2026-08', 1, 1),
        ],
      });

      expect(c.hasProjection()).toBe(false);
    });

    it('turns on at exactly three complete months and names the sample size', () => {
      const c = projectionFor({monthlyFlow: SKEWED_MONTHS});

      expect(c.hasProjection()).toBe(true);
      expect(c.projectionBasisLabel()).toBe('Median saved per month, based on 3 complete months');
    });

    it('keeps the in-progress month out of the baseline', () => {
      // August is wildly negative; if it leaked into the sample the median would move.
      const withWildAugust = projectionFor({
        monthlyFlow: [...SKEWED_MONTHS.slice(0, 3), flow('2026-08', 0, 90_000)],
        totalNetWorthUsd: 10_000,
      });

      expect(withWildAugust.projectedNetWorthFormatted()).toBe('$22,000');
      expect(withWildAugust.projectionBasisLabel()).toContain('3 complete months');
    });

    it('projects a shrinking net worth when the typical month is negative', () => {
      const c = projectionFor({
        monthlyFlow: [
          flow('2026-05', 1000, 1200),
          flow('2026-06', 1000, 1300),
          flow('2026-07', 1000, 1100),
        ],
        totalNetWorthUsd: 10_000,
      });

      expect(c.projectedNetWorthFormatted()).toBe('$7,600');
    });

    it('assumes no market return, even with market-marked sleeves present', () => {
      const c = projectionFor({
        monthlyFlow: SKEWED_MONTHS,
        totalNetWorthUsd: 10_000,
        netWorthHistory: [snapshot(100_000, 8000, 2000)],
      });

      // Contributions only: 10,000 + 12,000, with no return on the 10,000 market-marked sleeves.
      expect(c.projectedNetWorthFormatted()).toBe('$22,000');
    });
  });

  describe('net worth hero', () => {
    function history(first: number, last: number): NetWorthSnapshotDto[] {
      return [snapshot(first, 0, 0), snapshot(last, 0, 0)];
    }

    it('formats the headline symbol-first with cents', () => {
      const c = projectionFor({monthlyFlow: [], totalNetWorthUsd: 18_981.48});

      expect(c.totalBalanceFormatted()).toBe('$18,981.48');
    });

    it('states the window change in whole dollars and percent', () => {
      const c = projectionFor({monthlyFlow: [], netWorthHistory: history(10_000, 10_320)});

      expect(c.netWorthChangeFormatted()).toBe('+$320');
      expect(c.netWorthChangePercentFormatted()).toBe('+3.2%');
      expect(c.netWorthChangeDirection()).toBe(1);
    });

    it('signs a decline', () => {
      const c = projectionFor({monthlyFlow: [], netWorthHistory: history(10_000, 9_500)});

      expect(c.netWorthChangeFormatted()).toBe('-$500');
      expect(c.netWorthChangePercentFormatted()).toBe('-5.0%');
      expect(c.netWorthChangeDirection()).toBe(-1);
    });

    it('shows nothing until two snapshots exist', () => {
      const c = projectionFor({monthlyFlow: [], netWorthHistory: [snapshot(10_000, 0, 0)]});

      expect(c.netWorthChangeFormatted()).toBe('');
      expect(c.netWorthChangePercentFormatted()).toBe('');
      expect(c.netWorthChangeDirection()).toBe(0);
    });
  });
});
