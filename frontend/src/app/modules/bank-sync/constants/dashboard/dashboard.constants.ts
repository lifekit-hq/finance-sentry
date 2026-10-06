import {type HistoryRange} from '../../models/dashboard/dashboard.model';

/**
 * One selected range drives every dashboard widget — the net-worth chart via from/to
 * dates, the month-bucketed statistics (income vs spending, savings rate, top
 * categories) via this month count. 'ytd' is calendar-dependent, so it resolves through
 * `DashboardRangeUtils.months`. 'all' maps to the backend's maximum window.
 */
export const HISTORY_RANGE_MONTHS: Record<Exclude<HistoryRange, 'ytd'>, number> = {
  '1m': 1,
  '3m': 3,
  '6m': 6,
  '1y': 12,
  all: 120,
};

/** Short display suffix for range-scoped widget labels, e.g. "Top Spending Categories (3M)". */
export const HISTORY_RANGE_LABELS: Record<HistoryRange, string> = {
  '1m': '1M',
  '3m': '3M',
  '6m': '6M',
  ytd: 'YTD',
  '1y': '1Y',
  all: 'All',
};

/** Heading over the income / spending / savings tiles for each range. */
export const HISTORY_RANGE_TILE_HEADINGS: Record<HistoryRange, string> = {
  '1m': 'This month',
  '3m': 'Last 3 months',
  '6m': 'Last 6 months',
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
