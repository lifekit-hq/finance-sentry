import {computed, inject, type Signal} from '@angular/core';
import {type AreaSeries, type BarSeries, type DonutSegment} from '@lifekit-hq/ui';

import {CategoryStore} from '../../../../shared/store/categories/categories.store';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {
  MIN_PROJECTION_MONTHS,
  PROJECTION_HORIZON_MONTHS,
} from '../../constants/dashboard/dashboard.constants';
import {
  type DashboardData,
  type MonthlyFlow,
  type NetWorthSnapshotDto,
} from '../../models/dashboard/dashboard.model';

interface StateSignals {
  data: Signal<Nullable<DashboardData>>;
  netWorthHistory: Signal<NetWorthSnapshotDto[]>;
  historyLoading: Signal<boolean>;
  historyError: Signal<string | null>;
}

const MONTH_FORMATTER = new Intl.DateTimeFormat('en-US', {month: 'short'});
const DAY_FORMATTER = new Intl.DateTimeFormat('en-US', {month: 'short', day: 'numeric'});
const YEAR_SUFFIX_DIGITS = 2;

// "Jun '26", not "Jun 26" — a bare 2-digit year reads as a day of the month.
function formatMonthYear(date: Date): string {
  return `${MONTH_FORMATTER.format(date)} '${String(date.getUTCFullYear()).slice(-YEAR_SUFFIX_DIGITS)}`;
}

// Below this span the snapshots are effectively daily, so month-year labels ("Jul '26")
// collapse to a single repeated value — use day-level labels ("Jul 5") instead.
const SHORT_SPAN_DAYS = 92;
const MS_PER_DAY = 86_400_000;

const SLEEVE_COLOR = {banking: '#10b981', brokerage: '#6366f1', crypto: '#f59e0b'} as const;
const INCOME_COLOR = '#10b981';
const SPENDING_COLOR = '#ef4444';
const PERCENT = 100;

// Shown instead of a red "100% below pace" while nothing has landed yet: salary posts once,
// so an empty month early on is the normal state, not a shortfall.
const NO_INCOME_LABEL = 'No income yet this month';

// The month-to-date tiles compare against the average of this many complete months,
// prorated by how far into the current month we are. Three is enough to absorb a single
// odd month without reaching back to spending habits that no longer apply.
const PACE_BASELINE_MONTHS = 3;

// Spending is only paced once this many days of the month have elapsed: on day 2 the
// prorated baseline is a few percent of a month, so one rent payment reads as four-digit
// "over pace". Before that the chip stays neutral.
const MIN_PACE_DAYS = 7;

// Displayed pace magnitudes are capped here and worded ">200%" beyond it.
const MAX_PACE_PERCENT = 200;

// A month-to-date savings rate is only meaningful once the month's income has actually
// landed. Salary posts once, often on the last day, so before then the month holds a full
// run of spending against stray small credits and the rate reads in the hundreds of
// percent negative. Below this fraction of a normal month's income we show nothing rather
// than a number that is technically correct and completely misleading.
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

function wholeUsd(value: number): string {
  return MoneyUtils.format(value, 'USD', WHOLE_DOLLARS);
}

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

/**
 * Fraction of the current month already elapsed, in (0, 1]. Used to scale a
 * complete-month baseline down to something a month-to-date figure can be
 * compared against without reading as a collapse every time a month starts.
 */
function elapsedMonthFraction(now = new Date()): number {
  // Day-of-month, not whole days since the 1st: the current day counts, because
  // transactions post throughout it. On the last day this is exactly 1.
  const daysInMonth = new Date(
    Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 0)
  ).getUTCDate();
  return now.getUTCDate() / daysInMonth;
}

/**
 * Signed percentage difference of a month-to-date actual against the prorated average of
 * the trailing complete months. Null when there is no baseline to speak of — a first-ever
 * month has nothing to be ahead or behind of, and inventing a delta there would be noise.
 * Also null during the first days of the month, where proration amplifies a single posting.
 */
function paceDelta(actual: number, baselines: number[]): number | null {
  if (new Date().getUTCDate() < MIN_PACE_DAYS) {
    return null;
  }
  const usable = baselines.slice(-PACE_BASELINE_MONTHS).filter(v => v > 0);
  if (usable.length === 0) {
    return null;
  }
  const expected = (usable.reduce((a, b) => a + b, 0) / usable.length) * elapsedMonthFraction();
  if (expected <= 0) {
    return null;
  }
  return ((actual - expected) / expected) * PERCENT;
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

function currentMonth(rows: MonthlyFlow[]): Nullable<MonthTotals> {
  const key = currentMonthKey();
  const forMonth = rows.filter(r => r.month === key);
  if (forMonth.length === 0) {
    return null;
  }
  return forMonth.reduce<MonthTotals>(
    (acc, r) => ({
      inflow: acc.inflow + r.inflowUsd,
      outflow: acc.outflow + r.outflowUsd,
      familySupportOutflow: acc.familySupportOutflow + (r.familySupportOutflowUsd ?? 0),
      invested: acc.invested + (r.investedOutflowUsd ?? 0),
    }),
    {inflow: 0, outflow: 0, familySupportOutflow: 0, invested: 0}
  );
}

/** Savings rate for one month, as a percentage. */
function savingsRateOf(totals: MonthTotals): number {
  return ((totals.inflow - totals.outflow) / totals.inflow) * PERCENT;
}

/**
 * Renders a pace comparison for `cmn-stat-card`. The card colours on the SIGN of `delta`
 * (positive green / negative red) and picks its arrow from it too, so the number handed
 * over is "how good is this", not "which way did it move" — for spending those are
 * opposites. The wording carries the direction so the arrow never has to.
 */
function paceChip(
  deltaPercent: number | null,
  goodWhenAbove: boolean,
  above: string,
  below: string
): {delta: number | null; label: string} {
  if (deltaPercent === null) {
    return {delta: null, label: ''};
  }
  const rounded = Math.round(Math.abs(deltaPercent));
  const magnitude = Math.min(rounded, MAX_PACE_PERCENT);
  const shown = rounded > MAX_PACE_PERCENT ? `>${MAX_PACE_PERCENT}%` : `${magnitude}%`;
  const isAbove = deltaPercent >= 0;
  const isGood = isAbove === goodWhenAbove;
  return {
    delta: isGood ? magnitude : -magnitude,
    label: `${shown} ${isAbove ? above : below}`,
  };
}

/**
 * Savings-rate comparison chip. The gap between two rates is in percentage POINTS, so it
 * is worded that way rather than reusing paceChip's "% above" phrasing, which would claim
 * a percentage of a percentage.
 */
function savingsRateChip(
  monthToDateRate: number | null,
  months: [string, MonthTotals][]
): {delta: number | null; label: string} {
  const rates = months
    .filter(([, v]) => v.inflow > 0)
    .slice(-PACE_BASELINE_MONTHS)
    .map(([, v]) => savingsRateOf(v));
  if (monthToDateRate === null || rates.length === 0) {
    return {delta: null, label: ''};
  }
  const diff = monthToDateRate - rates.reduce((a, b) => a + b, 0) / rates.length;
  const points = Math.round(Math.abs(diff));
  return {
    delta: diff >= 0 ? points : -points,
    label: `${points} pts ${diff >= 0 ? 'above' : 'below'} usual`,
  };
}

export function dashboardComputed(store: StateSignals) {
  const categoryStore = inject(CategoryStore);

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

  const monthToDate = computed(() => currentMonth(store.data()?.monthlyFlow ?? []));

  const outflowPace = computed(() =>
    paceDelta(
      monthToDate()?.outflow ?? 0,
      completeMonths().map(([, v]) => v.outflow)
    )
  );

  // Rates are scale-free, so unlike the money figures they are compared against the plain
  // average of the closed months rather than a prorated one.
  const savingsRateMonthToDate = computed((): number | null => {
    const mtd = monthToDate();
    const baselineInflows = completeMonths()
      .map(([, v]) => v.inflow)
      .filter(v => v > 0)
      .slice(-PACE_BASELINE_MONTHS);
    if (!mtd || mtd.inflow <= 0 || baselineInflows.length === 0) {
      return null;
    }
    const normalInflow = baselineInflows.reduce((a, b) => a + b, 0) / baselineInflows.length;
    return mtd.inflow >= normalInflow * INCOME_LANDED_FRACTION ? savingsRateOf(mtd) : null;
  });

  // Income is lumpy (salary posts once), so prorating it is wrong at any point in the
  // month: no pace delta, only the neutral "nothing yet" note once there is history.
  const inflowChip = computed((): {delta: number | null; label: string} => {
    const hasBaseline = completeMonths().some(([, v]) => v.inflow > 0);
    if (hasBaseline && (monthToDate()?.inflow ?? 0) <= 0) {
      return {delta: 0, label: NO_INCOME_LABEL};
    }
    return {delta: null, label: ''};
  });
  const spendingChip = computed(() => paceChip(outflowPace(), false, 'over pace', 'under pace'));
  const savingsChip = computed(() => savingsRateChip(savingsRateMonthToDate(), completeMonths()));

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

  // Net-worth change across the loaded window, null until there are two usable snapshots.
  const netWorthChange = computed((): {delta: number; percent: number | null} | null => {
    const history = validHistory();
    if (history.length < MIN_POINTS_FOR_DELTA) {
      return null;
    }
    const start = history[0].totalNetWorth;
    const delta = history[history.length - 1].totalNetWorth - start;
    return {delta, percent: start > 0 ? (delta / start) * PERCENT : null};
  });

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

  return {
    totalBalanceFormatted: computed(() => MoneyUtils.format(store.data()?.totalNetWorthUsd ?? 0)),

    // Signed net-worth change across the loaded window, shown inline under the hero figure.
    netWorthChangeFormatted: computed(() => {
      const change = netWorthChange();
      return change ? MoneyUtils.format(change.delta, 'USD', {...WHOLE_DOLLARS, signed: true}) : '';
    }),
    netWorthChangePercentFormatted: computed(() => {
      const percent = netWorthChange()?.percent;
      return percent === null || percent === undefined
        ? ''
        : `${percent > 0 ? '+' : ''}${percent.toFixed(CHANGE_PERCENT_DIGITS)}%`;
    }),
    // Sign of the change, for colouring: 1 up, -1 down, 0 flat or unknown.
    netWorthChangeDirection: computed(() => Math.sign(netWorthChange()?.delta ?? 0)),

    monthlySpendingFormatted: computed(() => {
      const cur = monthToDate();
      return cur ? MoneyUtils.format(cur.outflow, 'USD') : '—';
    }),

    monthlyInflowFormatted: computed(() => {
      const cur = monthToDate();
      return cur ? MoneyUtils.format(cur.inflow, 'USD') : '—';
    }),

    savingsRateMonthToDateFormatted: computed(() => {
      const rate = savingsRateMonthToDate();
      return rate === null ? '—' : `${Math.round(rate)}%`;
    }),

    // Month-to-date against the trailing complete months, prorated by how far into the
    // month we are — otherwise a figure two days into August always looks like a collapse.
    inflowPaceDelta: computed(() => inflowChip().delta),
    inflowPaceLabel: computed(() => inflowChip().label),

    // Spending above pace is bad, so the card's colour is driven by the inverse.
    spendingPaceDelta: computed(() => spendingChip().delta),
    spendingPaceLabel: computed(() => spendingChip().label),

    savingsRatePaceDelta: computed(() => savingsChip().delta),
    savingsRatePaceLabel: computed(() => savingsChip().label),

    // Stacked net-worth composition (banking / brokerage / crypto) over time — the snapshots
    // already carry each sleeve, so we plot the mix rather than throwing it away for one line.
    netWorthAreaSeries: computed((): AreaSeries[] => {
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
          if (value > 0) {
            last = value;
          }
          return last;
        });
      };
      const toPoints = (values: number[]) => values.map((value, i) => ({label: labels[i], value}));
      return [
        {
          label: 'Banking',
          color: SLEEVE_COLOR.banking,
          points: toPoints(carryForward(s => s.bankingTotal)),
        },
        {
          label: 'Brokerage',
          color: SLEEVE_COLOR.brokerage,
          points: toPoints(carryForward(s => s.brokerageTotal)),
        },
        {
          label: 'Crypto',
          color: SLEEVE_COLOR.crypto,
          points: toPoints(carryForward(s => s.cryptoTotal)),
        },
      ];
    }),

    incomeVsSpendingBars: computed((): BarSeries[] => {
      const grouped = completeMonths();
      if (grouped.length === 0) {
        return [];
      }
      return [
        {
          label: 'Income',
          color: INCOME_COLOR,
          points: grouped.map(([key, v]) => ({label: formatMonthKey(key), value: v.inflow})),
        },
        {
          label: 'Spending',
          color: SPENDING_COLOR,
          points: grouped.map(([key, v]) => ({label: formatMonthKey(key), value: v.outflow})),
        },
      ];
    }),

    // Below three complete months the tile does not render at all — a projection off one or
    // two months is noise wearing a number's clothes.
    hasProjection,

    projectedNetWorthFormatted: computed(() =>
      wholeUsd((store.data()?.totalNetWorthUsd ?? 0) + projectedContributions())
    ),

    // Always plural: the line is gated at three months, so the singular can never surface.
    projectionBasisLabel: computed(
      () => `Median saved per month, based on ${completeMonthCount()} complete months`
    ),

    // Gated on the same complete-month window the charts plot: a user whose only data is
    // the in-progress month has nothing to chart yet, and rendering an empty frame reads
    // as a broken widget rather than as "not enough history".
    hasCashFlow: computed(() => completeMonths().length > 0),

    categoryChartData: computed((): DonutSegment[] =>
      (store.data()?.topCategories ?? []).map(c => ({
        label: categoryStore.labelMap()[c.category] ?? MerchantCategoryUtils.format(c.category),
        value: c.totalSpend,
      }))
    ),

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
