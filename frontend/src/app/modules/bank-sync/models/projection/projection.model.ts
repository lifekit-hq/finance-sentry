/**
 * A projected figure as a range (finance-framework C8): `central` at the central return assumption, `low` and
 * `high` the smallest and largest of the three scenarios, so `low <= central <= high` always holds.
 */
export interface ProjectionRange {
  low: number;
  central: number;
  high: number;
}

/** Mirrors the backend's `NetWorthProjectionStatus`, written by name. */
export type NetWorthProjectionStatus = 'Projected' | 'InsufficientHistory';

/**
 * The twelve-month net-worth projection (M10): the latest net worth plus the median monthly saving,
 * compounded at the shared real-return assumption, as a range in today's money. Rates are fractions
 * (0.05 = 5%); amounts are in `baseCurrency`.
 */
export interface NetWorthProjection {
  status: NetWorthProjectionStatus;
  horizonMonths: number;
  currentNetWorth: number;
  monthlySavings: number;
  /** How many complete months the median saving is taken over. */
  completeMonths: number;
  /** Present only while the status is `Projected`. */
  projectedNetWorth: Nullable<ProjectionRange>;
  realAnnualReturnRange: ProjectionRange;
  hasStaleSleeves: boolean;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
}
