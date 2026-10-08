import {computed, inject, type Signal} from '@angular/core';
import {CATEGORICAL_STEPS, NEUTRAL_STEP, seriesColor} from '@lifekit-hq/charts-core';
import {type AreaSeries, type BarSeries, type DonutSegment} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
  FLOW_IN_COLOUR,
  FLOW_OUT_COLOUR,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {
  MIN_PROJECTION_MONTHS,
  PROJECTION_HORIZON_MONTHS,
} from '../../constants/dashboard/dashboard.constants';
import {
  type DashboardData,
  type HistoryRange,
  type MonthlyFlow,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';

interface StateSignals {
  data: Signal<Nullable<DashboardData>>;
  historyRange: Signal<HistoryRange>;
  netWorthHistory: Signal<NetWorthSnapshotDto[]>;
  historyLoading: Signal<boolean>;
  historyError: Signal<string | null>;
  scrubIndex: Signal<number | null>;
}

const MONTH_FORMATTER = new Intl.DateTimeFormat('en-US', {month: 'short'});
const DAY_FORMATTER = new Intl.DateTimeFormat('en-US', {month: 'short', day: 'numeric'});
const FULL_DATE_FORMATTER = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
  timeZone: 'UTC',
});
const YEAR_SUFFIX_DIGITS = 2;

// "Jun '26", not "Jun 26" — a bare 2-digit year reads as a day of the month.
function formatMonthYear(date: Date): string {
  return `${MONTH_FORMATTER.format(date)} '${String(date.getUTCFullYear()).slice(-YEAR_SUFFIX_DIGITS)}`;
}

// Below this span the snapshots are effectively daily, so month-year labels ("Jul '26")
// collapse to a single repeated value — use day-level labels ("Jul 5") instead.
const SHORT_SPAN_DAYS = 92;
const MS_PER_DAY = 86_400_000;

// Sleeves keep the colour of the asset class they hold, and income / spending the money-in and
// money-out colours: none of them follow the theme. Resolved when a chart is built, since a canvas
// cannot read a CSS var.
export const SLEEVE_COLOUR = {
  banking: CASH_COLOUR,
  brokerage: EQUITY_COLOUR,
  crypto: CRYPTO_COLOUR,
} as const;
const INCOME_COLOUR = FLOW_IN_COLOUR;
const SPENDING_COLOUR = FLOW_OUT_COLOUR;
const PERCENT = 100;
const OTHER_CATEGORIES_LABEL = 'Other categories';

// A savings rate over a single month is only meaningful once the month's income has actually
// landed. Salary posts once, often on the last day, so before then the month holds a full run
// of spending against stray small credits and the rate reads in the hundreds of percent
// negative. Below this fraction of the window's spending we show nothing rather than a number
// that is technically correct and completely misleading. Longer windows dilute the partial
// month, so only the day-resolution windows (1W, MTD, 1M) are gated.
const INCOME_LANDED_FRACTION = 0.5;

// Need at least a start and end snapshot to state a change over the window.
const MIN_POINTS_FOR_DELTA = 2;

// An even-length sample has two middle values; the median is their average.
const MEDIAN_HALVES = 2;

/**
 * Median of a non-empty sample. The projection uses this rather than a mean because the
 * complete-month window is only a handful of months deep and one artifact month — June 2026
 * carries a duplicated salary (#400 audit) — moves a mean by hundreds of dollars a month
 * while leaving a median where the typical month actually sits.
 */
function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / MEDIAN_HALVES);
  return sorted.length % MEDIAN_HALVES === 0
    ? (sorted[mid - 1] + sorted[mid]) / MEDIAN_HALVES
    : sorted[mid];
}

// Cents on a twelve-month forecast are noise.
const WHOLE_DOLLARS = {maxFractionDigits: 0} as const;
const CHANGE_PERCENT_DIGITS = 1;

function currentMonthKey(): string {
  const now = new Date();
  const MONTH_KEY_PAD = 2;
  return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(MONTH_KEY_PAD, '0')}`;
}

function formatMonthKey(key: string): string {
  const [year, month] = key.split('-').map(Number);
  return formatMonthYear(new Date(Date.UTC(year, month - 1, 1)));
}

interface MonthTotals {
  inflow: number;
  outflow: number;
  familySupportOutflow: number;
  invested: number;
}

/** Collapse the per-currency monthly rows into one USD inflow/outflow per month, sorted. */
function groupMonthly(rows: MonthlyFlow[]): [string, MonthTotals][] {
  const byMonth = new Map<string, MonthTotals>();
  for (const r of rows) {
    const cur = byMonth.get(r.month) ?? {
      inflow: 0,
      outflow: 0,
      familySupportOutflow: 0,
      invested: 0,
    };
    cur.inflow += r.inflowUsd;
    cur.outflow += r.outflowUsd;
    cur.familySupportOutflow += r.familySupportOutflowUsd ?? 0;
    cur.invested += r.investedOutflowUsd ?? 0;
    byMonth.set(r.month, cur);
  }
  return [...byMonth.entries()].sort(([a], [b]) => a.localeCompare(b));
}

function sumTotals(rows: [string, MonthTotals][]): Nullable<MonthTotals> {
  if (rows.length === 0) {
    return null;
  }
  return rows.reduce<MonthTotals>(
    (acc, [, v]) => ({
      inflow: acc.inflow + v.inflow,
      outflow: acc.outflow + v.outflow,
      familySupportOutflow: acc.familySupportOutflow + v.familySupportOutflow,
      invested: acc.invested + v.invested,
    }),
    {inflow: 0, outflow: 0, familySupportOutflow: 0, invested: 0}
  );
}

/** Savings rate for one month, as a percentage. */
function savingsRateOf(totals: MonthTotals): number {
  return ((totals.inflow - totals.outflow) / totals.inflow) * PERCENT;
}

export function dashboardComputed(store: StateSignals) {
  const categoryStore = inject(CategoryStore);

  // Every dashboard total arrives already converted to the profile's base currency.
  const baseCurrency = computed(() => store.data()?.baseCurrency ?? DEFAULT_BASE_CURRENCY);

  // Snapshots with a real total; days with a missing feed land as 0 and would otherwise
  // render as a cliff down to the axis, reading as if net worth briefly vanished.
  const validHistory = computed(() => store.netWorthHistory().filter(s => s.totalNetWorth > 0));

  // Every month-bucketed CHART plots complete calendar months only. The in-progress month
  // is a fragment: as a bar it reads as income collapsing, and as a savings rate it swings
  // to absurd magnitudes. It belongs on the month-to-date tiles instead, where a partial
  // figure is exactly what the reader expects — the same split Binance and IBKR use, where
  // the current period is a tile and the bars are closed periods.
  const completeMonths = computed(() =>
    groupMonthly(store.data()?.monthlyFlow ?? []).filter(([key]) => key !== currentMonthKey())
  );

  // The tiles total every month in the selected window, in-progress month included: one
  // range, one story across the whole dashboard. (The charts still plot closed months only.)
  // The backend always returns the month before the window's first one too (its `months` clamps
  // to at least 1), so earlier buckets are cut here by month key. A day-resolution window
  // (1W, MTD, 1M) starts mid-month, so the backend totals exactly its days into `windowFlow`
  // and the tiles read that instead of whole-month buckets.
  const windowTotals = computed(() => {
    const data = store.data();
    if (DashboardRangeUtils.isDayWindow(store.historyRange()) && data?.windowFlow) {
      return sumTotals(groupMonthly(data.windowFlow));
    }
    const start = DashboardRangeUtils.windowStartKey(store.historyRange());
    return sumTotals(groupMonthly(data?.monthlyFlow ?? []).filter(([key]) => key >= start));
  });

  const windowSavingsRate = computed((): number | null => {
    const totals = windowTotals();
    if (!totals || totals.inflow <= 0) {
      return null;
    }
    const gated = DashboardRangeUtils.isDayWindow(store.historyRange());
    return gated && totals.inflow < totals.outflow * INCOME_LANDED_FRACTION
      ? null
      : savingsRateOf(totals);
  });

  // Forecasting the net-worth line itself would be forecasting the market — most of the book
  // is market-marked and its daily swings dwarf a month of savings. So the projection is built
  // from what the user actually controls (contributions) and assumes no market return.
  const completeMonthCount = computed(() => completeMonths().length);
  const hasProjection = computed(() => completeMonthCount() >= MIN_PROJECTION_MONTHS);

  const medianMonthlySavings = computed(() => {
    const months = completeMonths();
    return months.length === 0 ? 0 : median(months.map(([, v]) => v.inflow - v.outflow));
  });

  // Named once so the headline reads straight off the same figure the basis label describes.
  const projectedContributions = computed(() => medianMonthlySavings() * PROJECTION_HORIZON_MONTHS);

  const snapshotLabeller = computed((): ((s: NetWorthSnapshotDto) => string) => {
    const history = validHistory();
    if (history.length === 0) {
      return s => s.snapshotDate;
    }
    const times = history.map(s => new Date(s.snapshotDate).getTime());
    const spanDays = (Math.max(...times) - Math.min(...times)) / MS_PER_DAY;
    const label =
      spanDays <= SHORT_SPAN_DAYS ? (d: Date): string => DAY_FORMATTER.format(d) : formatMonthYear;
    return s => label(new Date(s.snapshotDate));
  });

  // Stacked net-worth composition (banking / brokerage / crypto) over time — the snapshots
  // already carry each sleeve, so we plot the mix rather than throwing it away for one line.
  const netWorthAreaSeries = computed((): AreaSeries[] => {
    const history = validHistory();
    if (history.length === 0) {
      return [];
    }
    const labelOf = snapshotLabeller();
    const labels = history.map(labelOf);
    // A sleeve reading 0 on a given day means its feed was missing, not that the
    // balance vanished — carry the last-known value forward so the stack doesn't
    // collapse to the axis and read as a crash.
    const carryForward = (pick: (s: NetWorthSnapshotDto) => number): number[] => {
      let last = 0;
      return history.map(s => {
        const value = pick(s);
        if (value !== 0) {
          last = value;
        }
        return last;
      });
    };
    const toPoints = (values: number[]) => values.map((value, i) => ({label: labels[i], value}));
    return [
      {
        label: 'Banking',
        color: ChartColorUtils.canvas(SLEEVE_COLOUR.banking),
        points: toPoints(carryForward(s => s.bankingTotal)),
      },
      {
        label: 'Brokerage',
        color: ChartColorUtils.canvas(SLEEVE_COLOUR.brokerage),
        points: toPoints(carryForward(s => s.brokerageTotal)),
      },
      {
        label: 'Crypto',
        color: ChartColorUtils.canvas(SLEEVE_COLOUR.crypto),
        points: toPoints(carryForward(s => s.cryptoTotal)),
      },
    ];
  });

  // The stack top the chart draws at each point, carried-forward sleeves included.
  const chartedTotals = computed(() => {
    const series = netWorthAreaSeries();
    return series.length === 0
      ? []
      : series[0].points.map((_, i) => series.reduce((sum, s) => sum + s.points[i].value, 0));
  });

  // The snapshot under a scrubbing pointer; null at rest or when the index is stale.
  const scrubbed = computed(() => {
    const index = store.scrubIndex();
    return index === null ? null : (validHistory()[index] ?? null);
  });

  // The charted total under a scrubbing pointer, so the hero reads what the chart draws.
  const scrubbedTotal = computed((): number | null => {
    const index = store.scrubIndex();
    return index === null || scrubbed() === null ? null : chartedTotals()[index];
  });

  // Net-worth change from the window start to the figure shown: while scrubbing, both ends come
  // off the charted series; at rest, off the snapshots. Null until there are two usable snapshots.
  const netWorthChange = computed((): {delta: number; percent: number | null} | null => {
    const history = validHistory();
    if (history.length < MIN_POINTS_FOR_DELTA) {
      return null;
    }
    const held = scrubbedTotal();
    const start = held === null ? history[0].totalNetWorth : chartedTotals()[0];
    const end = held ?? history[history.length - 1].totalNetWorth;
    const delta = end - start;
    return {delta, percent: start > 0 ? (delta / start) * PERCENT : null};
  });

  return {
    baseCurrency,
    // The scrubbed point's total while a pointer holds the chart, else the live net worth.
    totalBalanceFormatted: computed(() =>
      MoneyUtils.format(scrubbedTotal() ?? store.data()?.totalNetWorthUsd ?? 0, baseCurrency())
    ),
    // The scrubbed snapshot's date, replacing the range label beside the delta; null at rest.
    scrubDateFormatted: computed((): string | null => {
      const snapshot = scrubbed();
      return snapshot ? FULL_DATE_FORMATTER.format(new Date(snapshot.snapshotDate)) : null;
    }),

    // Signed net-worth change across the loaded window, shown inline under the hero figure.
    netWorthChangeFormatted: computed(() => {
      const change = netWorthChange();
      return change
        ? MoneyUtils.format(change.delta, baseCurrency(), {...WHOLE_DOLLARS, signed: true})
        : '';
    }),
    netWorthChangePercentFormatted: computed(() => {
      const percent = netWorthChange()?.percent;
      return percent === null || percent === undefined
        ? ''
        : `${percent > 0 ? '+' : ''}${percent.toFixed(CHANGE_PERCENT_DIGITS)}%`;
    }),
    // Sign of the change, for colouring: 1 up, -1 down, 0 flat or unknown.
    netWorthChangeDirection: computed(() => Math.sign(netWorthChange()?.delta ?? 0)),

    windowSpendingFormatted: computed(() => {
      const totals = windowTotals();
      return totals ? MoneyUtils.format(totals.outflow, baseCurrency()) : '—';
    }),

    windowInflowFormatted: computed(() => {
      const totals = windowTotals();
      return totals ? MoneyUtils.format(totals.inflow, baseCurrency()) : '—';
    }),

    windowSavingsRateFormatted: computed(() => {
      const rate = windowSavingsRate();
      return rate === null ? '—' : `${Math.round(rate)}%`;
    }),

    netWorthAreaSeries,

    incomeVsSpendingBars: computed((): BarSeries[] => {
      const grouped = completeMonths();
      if (grouped.length === 0) {
        return [];
      }
      return [
        {
          label: 'Income',
          color: ChartColorUtils.canvas(INCOME_COLOUR),
          points: grouped.map(([key, v]) => ({label: formatMonthKey(key), value: v.inflow})),
        },
        {
          label: 'Spending',
          color: ChartColorUtils.canvas(SPENDING_COLOUR),
          points: grouped.map(([key, v]) => ({label: formatMonthKey(key), value: v.outflow})),
        },
      ];
    }),

    // Below three complete months the tile does not render at all — a projection off one or
    // two months is noise wearing a number's clothes.
    hasProjection,

    projectedNetWorthFormatted: computed(() =>
      MoneyUtils.format(
        (store.data()?.totalNetWorthUsd ?? 0) + projectedContributions(),
        baseCurrency(),
        WHOLE_DOLLARS
      )
    ),

    // Always plural: the line is gated at three months, so the singular can never surface.
    projectionBasisLabel: computed(
      () => `Median saved per month, based on ${completeMonthCount()} complete months`
    ),

    // Gated on the same complete-month window the charts plot: a user whose only data is
    // the in-progress month has nothing to chart yet, and rendering an empty frame reads
    // as a broken widget rather than as "not enough history".
    hasCashFlow: computed(() => completeMonths().length > 0),

    // The donut cycles the categorical series colours, so past that many slices two categories
    // would share one; the tail folds into a single slate slice instead. The table lists them all.
    categoryChartData: computed((): DonutSegment[] => {
      const segments = (store.data()?.topCategories ?? []).map(c => ({
        label: categoryStore.labelMap()[c.category] ?? MerchantCategoryUtils.format(c.category),
        value: c.totalSpend,
      }));
      if (segments.length <= CATEGORICAL_STEPS.length) {
        return segments;
      }
      const named = segments.slice(0, CATEGORICAL_STEPS.length - 1);
      const tail = segments.slice(named.length);
      return [
        ...named,
        {
          label: OTHER_CATEGORIES_LABEL,
          value: tail.reduce((sum, s) => sum + s.value, 0),
          color: seriesColor(NEUTRAL_STEP),
        },
      ];
    }),

    netWorthStaleNotice: computed((): string | null => {
      const history = store.netWorthHistory();
      const latest = history[history.length - 1];
      const sleeves = latest?.staleSleeves?.trim();
      if (!sleeves) {
        return null;
      }
      const names = sleeves
        .split(',')
        .map(s => s.trim())
        .filter(Boolean)
        .map(s => s.charAt(0).toUpperCase() + s.slice(1));
      return `${names.join(' & ')} data is stale — carried forward from the last successful sync, not a real change.`;
    }),

    isHistoryLoading: computed(() => store.historyLoading()),
    historyErrorMessage: computed(() => store.historyError()),
  };
}
