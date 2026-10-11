import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';

/**
 * One selected range drives every dashboard widget — the net-worth chart via from/to
 * dates, the month-bucketed statistics (income vs spending, savings rate, top
 * categories) via this month count. 'ytd' is calendar-dependent, so it resolves through
 * `DashboardRangeUtils`, and so do the day-resolution ranges (1W, MTD, 1M), which start on a
 * calendar day: for those the number here is only how many complete months of history to load
 * (the previous month, which covers each of their start days). 'all' maps to the backend's
 * maximum window.
 */
export const HISTORY_RANGE_MONTHS: Record<Exclude<HistoryRange, 'ytd'>, number> = {
  '1w': 1,
  mtd: 1,
  '1m': 1,
  '3m': 3,
  '1y': 12,
  all: 120,
};

/** Short display suffix for range-scoped widget labels, e.g. "Top Spending Categories (3M)". */
export const HISTORY_RANGE_LABELS: Record<HistoryRange, string> = {
  '1w': '1W',
  mtd: 'MTD',
  '1m': '1M',
  '3m': '3M',
  ytd: 'YTD',
  '1y': '1Y',
  all: 'ALL',
};

/** Heading over the income / spending / savings tiles for each range. */
export const HISTORY_RANGE_TILE_HEADINGS: Record<HistoryRange, string> = {
  '1w': 'Last 7 days',
  mtd: 'Month to date',
  '1m': 'Past month',
  '3m': 'Last 3 months',
  ytd: 'Year to date',
  '1y': 'Last 12 months',
  all: 'All time',
};

/** How far forward the net-worth projection line looks. Fixed — there is no goal entity yet. */
export const PROJECTION_HORIZON_MONTHS = 12;

/**
 * A projection off one or two months is noise wearing a number's clothes, so the line stays
 * hidden until the complete-month window is at least this deep.
 */
export const MIN_PROJECTION_MONTHS = 3;

/**
 * Where a click on a net-worth band opens: the page that lists the accounts behind it. Keyed by
 * the series label the chart emits; the "No split" band is a total with no single owner page, so
 * it is absent and its click does nothing.
 */
export const NET_WORTH_SERIES_ROUTES: ReadonlyMap<string, AppRoute> = new Map([
  ['Banking', AppRoute.AccountsList],
  ['Cash', AppRoute.AccountsList],
  ['Brokerage', AppRoute.AccountsInvestments],
  ['Brokerage invested', AppRoute.AccountsInvestments],
  ['Crypto', AppRoute.AccountsInvestments],
  ['Crypto invested', AppRoute.AccountsInvestments],
]);

/** The transactions `type` each income-vs-spending series drills into. */
export const CASH_FLOW_SERIES_TYPES: ReadonlyMap<string, 'credit' | 'debit'> = new Map([
  ['Income', 'credit'],
  ['Spending', 'debit'],
]);
