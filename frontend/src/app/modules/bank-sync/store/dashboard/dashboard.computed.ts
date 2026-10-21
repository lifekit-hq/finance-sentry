import {computed, type Signal} from '@angular/core';
import {CATEGORICAL_STEPS} from '@lifekit-hq/charts-core';
import {type AreaSeries, type DonutSegment, type Nullable} from '@lifekit-hq/ui';

import {
  CASH_COLOUR,
  CRYPTO_COLOUR,
  EQUITY_COLOUR,
  NEUTRAL_COLOUR,
} from '../../../../shared/constants/chart-colour/chart-colour.constants';
import {APP_LOCALE, MS_PER_DAY} from '../../../../shared/constants/date/date.constants';
import {STALE_SOURCE_CODE} from '../../../../shared/constants/headline/headline.constants';
import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {type Headline, type MaybeHeadline} from '../../../../shared/models/headline/headline.model';
import {ChartColorUtils} from '../../../../shared/utils/chart-color.utils';
import {DateUtils} from '../../../../shared/utils/date.utils';
import {type DateRange, DateRangeUtils} from '../../../../shared/utils/date-range.utils';
import {HeadlineUtils} from '../../../../shared/utils/headline.utils';
import {MoneyUtils} from '../../../../shared/utils/money.utils';
import {NET_WORTH_SERIES_ROUTES} from '../../constants/dashboard/dashboard.constants';
import {
  type DashboardData,
  type HistoryRange,
  type MonthlyFlow,
  type NetWorthAllocationRow,
  type NetWorthScopeChangeDto,
  type NetWorthSnapshotDto,
  type NetWorthSplit,
} from '../../models/dashboard/dashboard.model';
import {DashboardRangeUtils} from '../../utils/dashboard-range.utils';
import {MonthKeyUtils} from '../../utils/month-key.utils';
import {NetWorthSplitUtils} from '../../utils/net-worth-split.utils';

interface StateSignals {
  isLoading: Signal<boolean>;
  hasError: Signal<boolean>;
  data: Signal<Nullable<DashboardData>>;
  dataRange: Signal<Nullable<HistoryRange>>;
  historyRange: Signal<HistoryRange>;
  netWorthHistory: Signal<NetWorthSnapshotDto[]>;
  historyLoadedRange: Signal<Nullable<HistoryRange>>;
  historyScopeChanges: Signal<NetWorthScopeChangeDto[]>;
  historyChangeHeadline: Signal<MaybeHeadline>;
  historyLoading: Signal<boolean>;
  historyError: Signal<string | null>;
  scrubIndex: Signal<number | null>;
}

const MONTH_FORMATTER = new Intl.DateTimeFormat(APP_LOCALE, {month: 'short'});
const DAY_FORMATTER = new Intl.DateTimeFormat(APP_LOCALE, {month: 'short', day: 'numeric'});
const FULL_DATE_FORMATTER = new Intl.DateTimeFormat(APP_LOCALE, {
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
// collapse to a single repeated value — use day-level labels ("5 Jul") instead.
const SHORT_SPAN_DAYS = 92;

// Sleeves keep the colour of the asset class they hold: none of them follow the theme. Resolved when a chart is built, since a canvas
// cannot read a CSS var.
export const SLEEVE_COLOUR = {
  banking: CASH_COLOUR,
  brokerage: EQUITY_COLOUR,
  crypto: CRYPTO_COLOUR,
} as const;
// The split chart's three bands keep the asset-class colours; days with no split are drawn as one
// neutral band so they read as "total only", not as a share of any class. Other sources take a
// series step of their own so they never read as that neutral band.
export const SPLIT_COLOUR = {
  cash: CASH_COLOUR,
  brokerageInvested: EQUITY_COLOUR,
  cryptoInvested: CRYPTO_COLOUR,
  other: {step: CATEGORICAL_STEPS[3]},
  unsplit: NEUTRAL_COLOUR,
} as const;
export const NET_WORTH_BY_SLEEVE_LABEL = 'Net worth by sleeve';
export const NET_WORTH_BY_SPLIT_LABEL = 'Net worth by cash and invested';
const PERCENT = 100;
const OTHER_SOURCES_LABEL = 'Other sources';

// A savings rate over a single month is only meaningful once the month's income has actually
// landed. Salary posts once, often on the last day, so before then the month holds a full run
// of spending against stray small credits and the rate reads in the hundreds of percent
// negative. Below this fraction of the window's spending we show nothing rather than a number
// that is technically correct and completely misleading. Longer windows dilute the partial
// month, so only the day-resolution windows (1W, MTD, 1M) are gated.
const INCOME_LANDED_FRACTION = 0.5;

// Need at least a start and end snapshot to state a change over the window.
const MIN_POINTS_FOR_DELTA = 2;

// Cents on a headline amount are noise.
const WHOLE_DOLLARS = {maxFractionDigits: 0} as const;
const CHANGE_PERCENT_DIGITS = 1;

interface Change {
  delta: number;
  percent: number | null;
}

function changeOf(start: number, end: number): Change {
  const delta = end - start;
  return {delta, percent: start > 0 ? (delta / start) * PERCENT : null};
}

function formatPercent(percent: number | null | undefined): string {
  return percent === null || percent === undefined
    ? ''
    : `${percent > 0 ? '+' : ''}${percent.toFixed(CHANGE_PERCENT_DIGITS)}%`;
}

function daysInMonth(key: string): number {
  const [year, month] = key.split('-').map(Number);
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
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
    groupMonthly(store.data()?.monthlyFlow ?? []).filter(
      ([key]) => key !== MonthKeyUtils.currentUtc()
    )
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

  // The cash / invested split of each charted snapshot; null on a day with no split.
  const splitHistory = computed((): Nullable<NetWorthSplit>[] =>
    validHistory().map(NetWorthSplitUtils.of)
  );

  // Once any charted day has a split the chart stacks cash / invested; a window with none keeps
  // the sleeve stack, so ranges reaching before the backfill lose nothing they show today.
  const hasSplit = computed(() => splitHistory().some(split => split !== null));

  // Stacked net-worth composition over time. A split day's bands (cash, the two invested sleeves,
  // and other sources' invested value when any) add up to its stored total. A day with no split is drawn as a single "No split" band (its sleeves summed), never
  // as zeros in the three bands and never dropped, so the stack top is the total on every day.
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
    const banking = carryForward(s => s.bankingTotal);
    const brokerage = carryForward(s => s.brokerageTotal);
    const crypto = carryForward(s => s.cryptoTotal);
    const other = history.map(NetWorthSplitUtils.otherSourcesTotal);
    const hasOther = other.some(value => value !== 0);
    const splits = splitHistory();
    if (!hasSplit()) {
      return [
        {
          label: 'Banking',
          color: ChartColorUtils.canvas(SLEEVE_COLOUR.banking),
          points: toPoints(banking),
        },
        {
          label: 'Brokerage',
          color: ChartColorUtils.canvas(SLEEVE_COLOUR.brokerage),
          points: toPoints(brokerage),
        },
        {
          label: 'Crypto',
          color: ChartColorUtils.canvas(SLEEVE_COLOUR.crypto),
          points: toPoints(crypto),
        },
        ...(hasOther
          ? [
              {
                label: OTHER_SOURCES_LABEL,
                color: ChartColorUtils.canvas(SPLIT_COLOUR.other),
                points: toPoints(other),
              },
            ]
          : []),
      ];
    }
    const bands: AreaSeries[] = [
      {
        label: 'Cash',
        color: ChartColorUtils.canvas(SPLIT_COLOUR.cash),
        points: toPoints(splits.map(split => split?.cash ?? 0)),
      },
      {
        label: 'Brokerage invested',
        color: ChartColorUtils.canvas(SPLIT_COLOUR.brokerageInvested),
        points: toPoints(splits.map(split => split?.brokerageInvested ?? 0)),
      },
      {
        label: 'Crypto invested',
        color: ChartColorUtils.canvas(SPLIT_COLOUR.cryptoInvested),
        points: toPoints(splits.map(split => split?.cryptoInvested ?? 0)),
      },
    ];
    if (splits.some(split => (split?.otherInvested ?? 0) !== 0)) {
      bands.push({
        label: OTHER_SOURCES_LABEL,
        color: ChartColorUtils.canvas(SPLIT_COLOUR.other),
        points: toPoints(splits.map(split => split?.otherInvested ?? 0)),
      });
    }
    if (splits.every(split => split !== null)) {
      return bands;
    }
    return [
      ...bands,
      {
        label: 'No split',
        color: ChartColorUtils.canvas(SPLIT_COLOUR.unsplit),
        points: toPoints(
          splits.map((split, i) =>
            split === null ? banking[i] + brokerage[i] + crypto[i] + other[i] : 0
          )
        ),
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
  const netWorthChange = computed((): Change | null => {
    const history = validHistory();
    if (history.length < MIN_POINTS_FOR_DELTA) {
      return null;
    }
    const held = scrubbedTotal();
    const start = held === null ? history[0].totalNetWorth : chartedTotals()[0];
    const end = held ?? history[history.length - 1].totalNetWorth;
    return changeOf(start, end);
  });

  // Which kinds of scope change (accounts linked or removed) fall inside the change's window: from
  // the first snapshot to the figure shown, so a scrub back before a link does not carry its note.
  // Removal is approximate (bank-account deactivation time) until per-source rows exist (R8).
  const netWorthScopeNote = computed((): string => {
    const history = validHistory();
    if (history.length < MIN_POINTS_FOR_DELTA) {
      return '';
    }
    const start = history[0].snapshotDate;
    const end = scrubbed()?.snapshotDate ?? history[history.length - 1].snapshotDate;
    const inWindow = store.historyScopeChanges().filter(c => c.at >= start && c.at <= end);
    const linked = inWindow.some(c => c.kind === 'linked');
    const removed = inWindow.some(c => c.kind === 'removed');
    if (linked && removed) {
      return 'Includes newly linked or removed accounts';
    }
    if (linked) {
      return 'Includes newly linked accounts';
    }
    return removed ? 'Includes removed accounts' : '';
  });

  // The live net worth as a typed record, with the latest snapshot's stale sources added when the
  // API's own record does not already name them (and one built around the amount on an older
  // build). The stale sources are the ones its per-source rows mark STALE_SOURCE, or its stale
  // sleeves on an older API build. Null while scrubbing: a historical point is not the live
  // figure, so it carries no badge and never reads N/A.
  const netWorthHeadline = computed((): Nullable<Headline> => {
    const data = store.data();
    if (scrubbedTotal() !== null || !data) {
      return null;
    }
    const history = store.netWorthHistory();
    const latest = history[history.length - 1];
    const sleeves = latest?.sources
      ? latest.sources.filter(s => s.reasons.includes(STALE_SOURCE_CODE)).map(s => s.sourceId)
      : (latest?.staleSleeves ?? '')
          .split(',')
          .map(s => s.trim())
          .filter(Boolean);
    const named = data.netWorthHeadline?.reasons.some(r => r.code === STALE_SOURCE_CODE) ?? false;
    return HeadlineUtils.withStaleSources(
      data.netWorthHeadline,
      named ? [] : sleeves,
      baseCurrency(),
      data.totalNetWorthUsd
    );
  });

  // The window's change as a typed record; null while scrubbing (the pointer measures its own
  // span) and when the API sent none.
  const netWorthChangeHeadline = computed((): Nullable<Headline> =>
    scrubbedTotal() === null ? (store.historyChangeHeadline() ?? null) : null
  );

  // The chart's bands at the point shown (the scrubbed day, else the latest): Home's "where it is"
  // rows, which double as the chart's legend. A band empty on that day is left out.
  const allocation = computed((): NetWorthAllocationRow[] => {
    const series = netWorthAreaSeries();
    if (series.length === 0) {
      return [];
    }
    const index =
      scrubbedTotal() === null ? series[0].points.length - 1 : (store.scrubIndex() ?? 0);
    const total = chartedTotals()[index] ?? 0;
    return series
      .map(band => ({
        label: band.label,
        color: band.color ?? '',
        value: band.points[index]?.value ?? 0,
      }))
      .filter(row => row.value !== 0)
      .map(row => ({
        ...row,
        percent: total > 0 ? (row.value / total) * PERCENT : null,
        route: NET_WORTH_SERIES_ROUTES.get(row.label) ?? null,
      }));
  });

  // Spending over the window so far at the pace of the closed months loaded (their spending per
  // day, times the window's days to today): what the window would have cost in a usual stretch.
  // Null over ALL, which has no start, and before any month has closed.
  const usualWindowSpending = computed((): Nullable<number> => {
    const {from} = DashboardRangeUtils.windowDates(store.historyRange());
    const closed = completeMonths();
    if (!from || closed.length === 0) {
      return null;
    }
    const closedDays = closed.reduce((sum, [key]) => sum + daysInMonth(key), 0);
    const closedSpending = closed.reduce((sum, [, totals]) => sum + totals.outflow, 0);
    const windowDays =
      Math.round((Date.parse(DateUtils.isoDateUtc(new Date())) - Date.parse(from)) / MS_PER_DAY) +
      1;
    return (closedSpending / closedDays) * windowDays;
  });

  const isFirstLoad = computed(() =>
    store.data() === null ? store.isLoading() : store.dataRange() !== store.historyRange()
  );
  const isHistoryFirstLoad = computed(() =>
    store.netWorthHistory().length === 0
      ? store.historyLoading()
      : store.historyLoadedRange() !== store.historyRange()
  );

  return {
    baseCurrency,
    netWorthHeadline,
    netWorthChangeHeadline,
    // The scrubbed point's total while a pointer holds the chart, else the live net worth; N/A
    // (never 0) when the API says the figure is unavailable.
    totalBalanceFormatted: computed(() =>
      HeadlineUtils.display(
        netWorthHeadline(),
        MoneyUtils.format(scrubbedTotal() ?? store.data()?.totalNetWorthUsd ?? 0, baseCurrency())
      )
    ),
    // True when the change line has no figure but its record says why (a withheld change
    // reads "N/A" with its badge, never a 0 or a blank).
    netWorthChangeUnavailable: computed(
      (): boolean =>
        HeadlineUtils.isUnavailable(netWorthChangeHeadline()) && netWorthChange() === null
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
    // A percentage across a scope change measures the book growing or shrinking, not the money
    // moving, so it is withheld (the amount stays) and the note below says why.
    netWorthChangePercentFormatted: computed(() =>
      netWorthScopeNote() ? '' : formatPercent(netWorthChange()?.percent)
    ),
    netWorthScopeNote,
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

    // How far the window's spending sits from the usual pace, in words; null with no usual to
    // compare against.
    windowSpendingVsUsual: computed((): Nullable<string> => {
      const usual = usualWindowSpending();
      const totals = windowTotals();
      if (usual === null || !totals) {
        return null;
      }
      const gap = totals.outflow - usual;
      const amount = MoneyUtils.format(Math.abs(gap), baseCurrency(), WHOLE_DOLLARS);
      return `${amount} ${gap > 0 ? 'above' : 'below'} your normal`;
    }),
    // The window's savings rate, rounded, for the income tile's "Saved N%"; null as the tiles show "—".
    windowSavingsRatePercent: computed((): Nullable<number> => {
      const rate = windowSavingsRate();
      return rate === null ? null : Math.round(rate);
    }),
    allocation,
    // The donut beside the "where it is" rows: the bands holding money (a negative band has no slice).
    allocationSegments: computed((): DonutSegment[] =>
      allocation()
        .filter(row => row.value > 0)
        .map(row => ({label: row.label, value: row.value, color: row.color}))
    ),
    // The days the net worth bridge explains on Home: the range chips' window, up to today.
    bridgeWindow: computed(
      (): DateRange => DateRangeUtils.fromRelativeRange(store.historyRange()),
      {equal: (a, b) => a.from === b.from && a.to === b.to}
    ),

    netWorthAreaSeries,
    netWorthChartLabel: computed(() =>
      hasSplit() ? NET_WORTH_BY_SPLIT_LABEL : NET_WORTH_BY_SLEEVE_LABEL
    ),

    isHistoryLoading: computed(() => store.historyLoading()),
    // Keep content: a widget swaps for its skeleton while it has nothing to show for the selected
    // range. A same-range refresh or a revisit keeps the numbers on screen until the new ones land;
    // figures loaded for another range never sit under this range's label, so a range change shows
    // the skeletons until its own figures arrive.
    isFirstLoad,
    isHistoryFirstLoad,
    // The placeholder is still owed (another range's figures never sit under this label) but the
    // request has settled in failure, so it must not shimmer as if figures were on the way.
    isFirstLoadFailed: computed(() => isFirstLoad() && !store.isLoading() && store.hasError()),
    isHistoryFirstLoadFailed: computed(
      () => isHistoryFirstLoad() && !store.historyLoading() && store.historyError() !== null
    ),
    historyErrorMessage: computed(() => store.historyError()),
  };
}
