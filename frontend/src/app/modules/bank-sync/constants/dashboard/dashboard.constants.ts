import {type UrlParamCodec} from '@lifekit-hq/core';

import {AppRoute} from '../../../../shared/enums/app-route/app-route.enum';
import {type HistoryRange} from '../../models/dashboard/dashboard.model';

export const HISTORY_RANGES: readonly HistoryRange[] = [
  '1w',
  'mtd',
  '1m',
  '3m',
  'ytd',
  '1y',
  'all',
];

/** Source ids of the three legacy sleeve columns on a history row. */
export const SLEEVE_SOURCE_IDS: readonly string[] = ['banking', 'brokerage', 'crypto'];

export const DEFAULT_HISTORY_RANGE: HistoryRange = '3m';

/** The `range` query param: an unknown value reads as the default range. */
export const HISTORY_RANGE_CODEC: UrlParamCodec<HistoryRange> = {
  encode: range => range,
  decode: raw => HISTORY_RANGES.find(range => range === raw) ?? DEFAULT_HISTORY_RANGE,
};

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

/**
 * Where a click on a net-worth band opens: the page that lists the accounts behind it. Keyed by
 * the series label the chart emits; the "No split" band is a total with no single owner page, so
 * it is absent and its click does nothing.
 */
export const NET_WORTH_SERIES_ROUTES: ReadonlyMap<string, AppRoute> = new Map([
  ['Banking', AppRoute.AccountsList],
  ['Cash', AppRoute.AccountsList],
  ['Brokerage', AppRoute.Accounts],
  ['Brokerage invested', AppRoute.Accounts],
  ['Crypto', AppRoute.Accounts],
  ['Crypto invested', AppRoute.Accounts],
]);

/** The transactions `type` each income-vs-spending series drills into. */
export const CASH_FLOW_SERIES_TYPES: ReadonlyMap<string, 'credit' | 'debit'> = new Map([
  ['Income', 'credit'],
  ['Spending', 'debit'],
]);
