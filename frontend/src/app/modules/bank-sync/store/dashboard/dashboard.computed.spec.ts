import {provideHttpClient} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type DashboardData,
  type HistoryRange,
  type MonthlyFlow,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {dashboardComputed} from './dashboard.computed';

// Frozen mid-month so "the current month" is genuinely partial.
const NOW = new Date('2026-08-12T00:00:00.000Z');

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
  windowFlow?: MonthlyFlow[];
  totalNetWorthUsd?: number;
  netWorthHistory?: NetWorthSnapshotDto[];
  baseCurrency?: string;
}

function build(
  {monthlyFlow, windowFlow, totalNetWorthUsd = 0, netWorthHistory = [], baseCurrency}: Fixture,
  historyRange: HistoryRange = '3m'
) {
  const data: DashboardData = {
    aggregatedBalance: {USD: 0},
    totalNetWorthUsd,
    accountCount: 1,
    accountsByType: {},
    monthlyFlow,
    windowFlow,
    topCategories: [],
    lastSyncTimestamp: null,
    baseCurrency,
  } as unknown as DashboardData;

  return {
    data: signal<Nullable<DashboardData>>(data),
    historyRange: signal<HistoryRange>(historyRange),
    netWorthHistory: signal<NetWorthSnapshotDto[]>(netWorthHistory),
    historyLoading: signal(false),
    historyError: signal<string | null>(null),
  };
}

function computedFor(monthlyFlow: MonthlyFlow[], range: HistoryRange = '3m') {
  return TestBed.runInInjectionContext(() => dashboardComputed(build({monthlyFlow}, range)));
}

function computedForWindow(
  monthlyFlow: MonthlyFlow[],
  windowFlow: MonthlyFlow[] | undefined,
  range: HistoryRange
) {
  return TestBed.runInInjectionContext(() =>
    dashboardComputed(build({monthlyFlow, windowFlow}, range))
  );
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
  describe('base currency (#851)', () => {
    const euroFixture: Fixture = {
      monthlyFlow: [flow('2026-08', 3000, 3100)],
      totalNetWorthUsd: 1000,
      baseCurrency: 'EUR',
    };

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

    it('formats the headline and hero totals in the base currency the API names', () => {
      const c = projectionFor(euroFixture);

      expect(c.windowSpendingFormatted()).toBe('€3,100.00');
      expect(c.windowInflowFormatted()).toBe('€3,000.00');
      expect(c.totalBalanceFormatted()).toBe('€1,000.00');
      expect(c.baseCurrency()).toBe('EUR');
    });

    it('stays in dollars when the API names no base currency', () => {
      const c = projectionFor({...euroFixture, baseCurrency: undefined});

      expect(c.windowSpendingFormatted()).toBe('$3,100.00');
      expect(c.baseCurrency()).toBe('USD');
    });
  });

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

  describe('tiles total the selected window', () => {
    it('sums the window months, in-progress month included, and drops older buckets', () => {
      // 3M in August spans June–August; the backend also sends May.
      const c = computedFor(steadyHistory(500, 900));

      expect(c.windowInflowFormatted()).toBe('$8,500.00');
      expect(c.windowSpendingFormatted()).toBe('$4,900.00');
    });

    it('reports just the current month for month to date', () => {
      // The backend always returns the previous month alongside the current one.
      const c = computedFor([flow('2026-07', 4000, 2000), flow('2026-08', 500, 169.42)], 'mtd');

      expect(c.windowInflowFormatted()).toBe('$500.00');
      expect(c.windowSpendingFormatted()).toBe('$169.42');
    });

    it.each(['1w', 'mtd', '1m'] as const)(
      'totals the backend day-window flow, not whole-month buckets, for %s',
      range => {
        // Whole-month history would say 4000 in / 2000 out for July and 500 / 200 for August;
        // the day window is a slice of it and is what the tiles must show.
        const c = computedForWindow(
          [flow('2026-07', 4000, 2000), flow('2026-08', 500, 200)],
          [flow('2026-07', 100, 40), flow('2026-08', 50, 10)],
          range
        );

        expect(c.windowInflowFormatted()).toBe('$150.00');
        expect(c.windowSpendingFormatted()).toBe('$50.00');
      }
    );

    it('falls back to the month buckets for a day window the backend sent no flow for', () => {
      const c = computedForWindow([flow('2026-08', 500, 200)], undefined, 'mtd');

      expect(c.windowInflowFormatted()).toBe('$500.00');
    });

    it('ignores the day-window flow for month-based ranges', () => {
      const c = computedForWindow(steadyHistory(500, 900), [flow('2026-08', 1, 1)], '3m');

      expect(c.windowInflowFormatted()).toBe('$8,500.00');
    });

    it('gates the savings rate on every day window while income has not landed', () => {
      const c = computedForWindow([], [flow('2026-08', 200, 900)], '1w');

      expect(c.windowSavingsRateFormatted()).toBe('—');
    });

    it('starts year-to-date at January', () => {
      const c = computedFor(
        [flow('2025-12', 9000, 9000), flow('2026-01', 100, 40), flow('2026-08', 200, 60)],
        'ytd'
      );

      expect(c.windowInflowFormatted()).toBe('$300.00');
      expect(c.windowSpendingFormatted()).toBe('$100.00');
    });

    it('falls back to an em dash when the window has no rows', () => {
      const c = computedFor([]);

      expect(c.windowInflowFormatted()).toBe('—');
      expect(c.windowSpendingFormatted()).toBe('—');
    });
  });

  describe('window savings rate', () => {
    it('withholds the month-to-date rate while income has not landed', () => {
      // $200 against $900 of spending — salary has not posted, so the raw rate would
      // read about -350% and mean nothing.
      const c = computedFor([flow('2026-08', 200, 900)], 'mtd');

      expect(c.windowSavingsRateFormatted()).toBe('—');
    });

    it("ignores last month's income when gating the month-to-date rate", () => {
      const c = computedFor([flow('2026-07', 4000, 2000), flow('2026-08', 200, 900)], 'mtd');

      expect(c.windowSavingsRateFormatted()).toBe('—');
    });

    it('reports the month-to-date rate once income has substantially landed', () => {
      const c = computedFor([flow('2026-08', 4000, 1000)], 'mtd');

      expect(c.windowSavingsRateFormatted()).toBe('75%');
    });

    it('reports the rate over a longer window without gating', () => {
      // (4000 + 4000 + 200 - 2000 - 2000 - 900) / 8200 ≈ 40%.
      const c = computedFor(steadyHistory(200, 900));

      expect(c.windowSavingsRateFormatted()).toBe('40%');
    });

    it('withholds the rate when the window has no income', () => {
      const c = computedFor([flow('2026-08', 0, 300)]);

      expect(c.windowSavingsRateFormatted()).toBe('—');
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
