import {provideHttpClient, withXhr} from '@angular/common/http';
import {provideHttpClientTesting} from '@angular/common/http/testing';
import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {provideApiBaseUrl} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type CategoryStat,
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
  topCategories?: CategoryStat[];
  scrubIndex?: number | null;
  cashUsd?: number | null;
  investedUsd?: number | null;
}

function build(
  {
    monthlyFlow,
    windowFlow,
    totalNetWorthUsd = 0,
    netWorthHistory = [],
    baseCurrency,
    topCategories = [],
    scrubIndex = null,
    cashUsd,
    investedUsd,
  }: Fixture,
  historyRange: HistoryRange = '3m'
) {
  const data: DashboardData = {
    aggregatedBalance: {USD: 0},
    totalNetWorthUsd,
    accountCount: 1,
    accountsByType: {},
    monthlyFlow,
    windowFlow,
    topCategories,
    lastSyncTimestamp: null,
    baseCurrency,
    cashUsd,
    investedUsd,
  } as unknown as DashboardData;

  return {
    data: signal<Nullable<DashboardData>>(data),
    historyRange: signal<HistoryRange>(historyRange),
    netWorthHistory: signal<NetWorthSnapshotDto[]>(netWorthHistory),
    historyLoading: signal(false),
    historyError: signal<string | null>(null),
    scrubIndex: signal<number | null>(scrubIndex),
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
          provideHttpClient(withXhr()),
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
        provideHttpClient(withXhr()),
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

    describe('invested / cash sub-line', () => {
      it('reads invested then cash in whole dollars, leaving the headline as is', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 10_000.5,
          cashUsd: 700.4,
          investedUsd: 9_300,
        });

        expect(c.bookSplitFormatted()).toBe('invested $9,300 · cash $700');
        expect(c.totalBalanceFormatted()).toBe('$10,000.50');
      });

      it('shows negative cash, as card debt nets against it', () => {
        const c = projectionFor({monthlyFlow: [], cashUsd: -1_200, investedUsd: 5_000});

        expect(c.bookSplitFormatted()).toBe('invested $5,000 · cash -$1,200');
      });

      it('follows the base currency', () => {
        const c = projectionFor({
          monthlyFlow: [],
          baseCurrency: 'EUR',
          cashUsd: 500,
          investedUsd: 500,
        });

        expect(c.bookSplitFormatted()).toBe('invested €500 · cash €500');
      });

      it('is empty when the book split is unknown', () => {
        expect(projectionFor({monthlyFlow: []}).bookSplitFormatted()).toBeNull();
        expect(
          projectionFor({monthlyFlow: [], cashUsd: null, investedUsd: null}).bookSplitFormatted()
        ).toBeNull();
      });

      it('is empty while scrubbing, as the split is live only', () => {
        const c = projectionFor({
          monthlyFlow: [],
          cashUsd: 700,
          investedUsd: 9_300,
          netWorthHistory: [snapshot(10_000, 0, 0), snapshot(12_000, 0, 0)],
          scrubIndex: 1,
        });

        expect(c.bookSplitFormatted()).toBeNull();
      });
    });

    describe('while scrubbing', () => {
      const points = [snapshot(10_000, 0, 0), snapshot(12_000, 0, 0), snapshot(11_000, 0, 0)];

      it('shows the scrubbed point and its change since the window start', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$12,000.00');
        expect(c.netWorthChangeFormatted()).toBe('+$2,000');
        expect(c.netWorthChangePercentFormatted()).toBe('+20.0%');
        expect(c.scrubDateFormatted()).toBe('Jul 31, 2026');
      });

      it('colours the delta by the scrubbed point, not the window end', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [snapshot(10_000, 0, 0), snapshot(8_000, 0, 0), snapshot(11_000, 0, 0)],
          scrubIndex: 1,
        });

        expect(c.netWorthChangeDirection()).toBe(-1);
      });

      it('rests on the live figure when nothing is scrubbed', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
        });

        expect(c.totalBalanceFormatted()).toBe('$11,000.00');
        expect(c.netWorthChangeFormatted()).toBe('+$1,000');
        expect(c.scrubDateFormatted()).toBeNull();
      });

      it('reads the charted total on a day a sleeve feed went missing', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 40_000,
          netWorthHistory: [snapshot(40_000, 5_000, 0), snapshot(40_000, 0, 0)],
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$45,000.00');
        expect(c.netWorthChangeFormatted()).toBe('$0');
        expect(c.netWorthChangeDirection()).toBe(0);
      });

      it('keeps a negative sleeve in the scrubbed total and its delta', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 8_000,
          netWorthHistory: [snapshot(1_000, 10_000, 0), snapshot(-2_000, 10_000, 0)],
          scrubIndex: 1,
        });

        expect(c.totalBalanceFormatted()).toBe('$8,000.00');
        expect(c.netWorthChangeFormatted()).toBe('-$3,000');
        expect(c.netWorthChangeDirection()).toBe(-1);
      });

      it('ignores an index past the drawn history', () => {
        const c = projectionFor({
          monthlyFlow: [],
          totalNetWorthUsd: 11_000,
          netWorthHistory: points,
          scrubIndex: 9,
        });

        expect(c.totalBalanceFormatted()).toBe('$11,000.00');
        expect(c.scrubDateFormatted()).toBeNull();
      });
    });

    it('shows nothing until two snapshots exist', () => {
      const c = projectionFor({monthlyFlow: [], netWorthHistory: [snapshot(10_000, 0, 0)]});

      expect(c.netWorthChangeFormatted()).toBe('');
      expect(c.netWorthChangePercentFormatted()).toBe('');
      expect(c.netWorthChangeDirection()).toBe(0);
    });
  });

  describe('cash / invested split', () => {
    // A day with a split: the three parts add up to the stored total.
    function splitDay(
      date: string,
      cash: number,
      brokerageInvested: number,
      cryptoInvested: number
    ): NetWorthSnapshotDto {
      return {
        snapshotDate: date,
        bankingTotal: cash,
        brokerageTotal: brokerageInvested,
        cryptoTotal: cryptoInvested,
        totalNetWorth: cash + brokerageInvested + cryptoInvested,
        currency: 'USD',
        cashTotal: cash,
        brokerageInvested,
        cryptoInvested,
      };
    }

    function noSplitDay(date: string, banking: number, brokerage: number, crypto: number) {
      return {...snapshot(banking, brokerage, crypto), snapshotDate: date};
    }

    const values = (series: {points: {value: number}[]}) => series.points.map(p => p.value);

    describe('area chart series', () => {
      it('keeps the sleeve stack when no day has a split', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [snapshot(1_000, 2_000, 300), snapshot(1_100, 2_100, 310)],
        });

        expect(c.netWorthAreaSeries().map(s => s.label)).toEqual([
          'Banking',
          'Brokerage',
          'Crypto',
        ]);
        expect(c.netWorthChartLabel()).toBe('Net worth by sleeve');
      });

      it('stacks cash, brokerage invested and crypto invested when every day has a split', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
            splitDay('2026-08-02', 4_500, 5_200, 900),
          ],
        });
        const series = c.netWorthAreaSeries();

        expect(series.map(s => s.label)).toEqual(['Cash', 'Brokerage invested', 'Crypto invested']);
        expect(values(series[0])).toEqual([4_000, 4_500]);
        expect(values(series[1])).toEqual([5_000, 5_200]);
        expect(values(series[2])).toEqual([1_000, 900]);
        expect(c.netWorthChartLabel()).toBe('Net worth by cash and invested');
      });

      it('stacks to the stored total on every split day, a negative cash band included', () => {
        const history = [
          splitDay('2026-08-01', -500, 8_000, 2_000),
          splitDay('2026-08-02', 300, 8_100, 2_000),
        ];
        const series = projectionFor({
          monthlyFlow: [],
          netWorthHistory: history,
        }).netWorthAreaSeries();

        history.forEach((day, i) => {
          expect(series.reduce((sum, s) => sum + s.points[i].value, 0)).toBe(day.totalNetWorth);
        });
      });

      it('draws a day with no split as one "No split" band, keeping the stack top at the total', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            noSplitDay('2026-07-01', 3_000, 5_000, 500),
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
          ],
        });
        const series = c.netWorthAreaSeries();

        expect(series.map(s => s.label)).toEqual([
          'Cash',
          'Brokerage invested',
          'Crypto invested',
          'No split',
        ]);
        // Neither dropped nor attributed to a class: the three class bands are empty that day.
        expect(series.slice(0, 3).map(s => s.points[0].value)).toEqual([0, 0, 0]);
        expect(values(series[3])).toEqual([8_500, 0]);
        expect(series.map(s => s.points.length)).toEqual([2, 2, 2, 2]);
        expect(series.reduce((sum, s) => sum + s.points[0].value, 0)).toBe(8_500);
        expect(series.reduce((sum, s) => sum + s.points[1].value, 0)).toBe(10_000);
      });

      it('treats a partly null split as no split', () => {
        const partial = {...splitDay('2026-07-01', 4_000, 5_000, 1_000), cryptoInvested: null};
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [partial, splitDay('2026-08-01', 4_000, 5_000, 1_000)],
        });

        expect(values(c.netWorthAreaSeries()[3])).toEqual([10_000, 0]);
      });

      it('still carries a missing sleeve forward on the no-split span', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            noSplitDay('2026-07-01', 3_000, 5_000, 500),
            noSplitDay('2026-07-02', 3_000, 0, 500),
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
          ],
        });

        expect(values(c.netWorthAreaSeries()[3])).toEqual([8_500, 8_500, 0]);
      });
    });

    describe('invested change', () => {
      it('is the change in brokerage + crypto positions, cash excluded', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
            splitDay('2026-08-02', 9_000, 5_400, 1_100),
          ],
        });

        // Net worth rose 5,500 mostly on cash; the invested book rose 6,000 -> 6,500.
        expect(c.netWorthChangeFormatted()).toBe('+$5,500');
        expect(c.investedChangeFormatted()).toBe('+$500');
        expect(c.investedChangePercentFormatted()).toBe('+8.3%');
        expect(c.investedChangeDirection()).toBe(1);
      });

      it('signs a decline', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
            splitDay('2026-08-02', 4_000, 4_500, 1_000),
          ],
        });

        expect(c.investedChangeFormatted()).toBe('-$500');
        expect(c.investedChangePercentFormatted()).toBe('-8.3%');
        expect(c.investedChangeDirection()).toBe(-1);
      });

      it('runs only over the days that have a split', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            noSplitDay('2026-07-01', 100, 100, 100),
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
            splitDay('2026-08-02', 4_000, 5_500, 1_000),
            noSplitDay('2026-08-03', 100, 100, 100),
          ],
        });

        expect(c.investedChangeFormatted()).toBe('+$500');
      });

      it('is hidden when the range has fewer than two days with a split', () => {
        const none = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [snapshot(1_000, 2_000, 300), snapshot(1_100, 2_100, 310)],
        });
        const one = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            noSplitDay('2026-07-01', 100, 100, 100),
            splitDay('2026-08-01', 4_000, 5_000, 1_000),
          ],
        });

        for (const c of [none, one]) {
          expect(c.investedChangeFormatted()).toBe('');
          expect(c.investedChangePercentFormatted()).toBe('');
          expect(c.investedChangeDirection()).toBe(0);
        }
      });

      it('states the amount without a percent when the invested book started empty', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            splitDay('2026-08-01', 4_000, 0, 0),
            splitDay('2026-08-02', 3_000, 1_000, 0),
          ],
        });

        expect(c.investedChangeFormatted()).toBe('+$1,000');
        expect(c.investedChangePercentFormatted()).toBe('');
      });

      it('names the first split day when it is after the start of the window', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            noSplitDay('2026-07-01', 100, 100, 100),
            splitDay('2026-07-08', 4_000, 5_000, 1_000),
            splitDay('2026-07-09', 4_000, 5_500, 1_000),
          ],
        });

        expect(c.investedChangeSinceFormatted()).toBe('since Jul 8, 2026');
      });

      it('leaves the window to the range label when the split covers all of it', () => {
        const c = projectionFor({
          monthlyFlow: [],
          netWorthHistory: [
            splitDay('2026-07-08', 4_000, 5_000, 1_000),
            splitDay('2026-07-09', 4_000, 5_500, 1_000),
          ],
        });

        expect(c.investedChangeSinceFormatted()).toBeNull();
      });

      describe('while scrubbing', () => {
        const days = [
          noSplitDay('2026-07-01', 100, 100, 100),
          splitDay('2026-08-01', 4_000, 5_000, 1_000),
          splitDay('2026-08-02', 4_000, 5_600, 1_000),
          splitDay('2026-08-03', 4_000, 5_000, 1_500),
        ];

        it('runs from the first split day to the scrubbed day', () => {
          const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex: 2});

          expect(c.investedChangeFormatted()).toBe('+$600');
        });

        it('is hidden on a day with no split and on the first split day', () => {
          for (const scrubIndex of [0, 1]) {
            const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex});

            expect(c.investedChangeFormatted()).toBe('');
          }
        });

        it('ignores an index past the drawn history', () => {
          const c = projectionFor({monthlyFlow: [], netWorthHistory: days, scrubIndex: 9});

          expect(c.investedChangeFormatted()).toBe('');
        });
      });
    });
  });

  describe('top spending categories donut', () => {
    function categories(count: number): CategoryStat[] {
      return Array.from({length: count}, (_, i) => ({
        category: `cat-${i}`,
        totalSpend: 100 - i,
        percentOfTotal: 0,
      }));
    }

    it('draws every category while each can keep its own colour', () => {
      const c = projectionFor({monthlyFlow: [], topCategories: categories(8)});

      expect(c.categoryChartData().map(s => s.value)).toEqual([100, 99, 98, 97, 96, 95, 94, 93]);
      expect(c.categoryChartData().every(s => !('color' in s))).toBe(true);
    });

    it('folds the tail past seven into one slate slice', () => {
      const c = projectionFor({monthlyFlow: [], topCategories: categories(10)});
      const segments = c.categoryChartData();

      expect(segments).toHaveLength(8);
      expect(segments.slice(0, 7).map(s => s.value)).toEqual([100, 99, 98, 97, 96, 95, 94]);
      expect(segments[7]).toEqual({
        label: 'Other categories',
        value: 93 + 92 + 91,
        color: '#636a6d',
      });
    });
  });
});
