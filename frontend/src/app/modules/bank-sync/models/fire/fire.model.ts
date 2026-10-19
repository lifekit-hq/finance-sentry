import {type ProjectionRange} from '../projection/projection.model';

/** Mirrors the backend's `FireProjectionStatus`, written by name. */
export type FireProjectionStatus =
  'Projected' | 'AlreadyReached' | 'NotSaving' | 'InsufficientHistory';

/**
 * Everything the tile needs to state its own arithmetic in words. Rates are fractions
 * (0.04 = 4%); amounts are in `baseCurrency`.
 */
export interface FireProjection {
  status: FireProjectionStatus;
  target: number;
  currentNetWorth: number;
  monthlySavings: number;
  annualSpend: number;
  safeWithdrawalRate: number;
  realAnnualReturn: number;
  /** ISO date (`yyyy-MM-dd`), present only while the status is `Projected`. */
  projectedDate: Nullable<string>;
  monthsToFire: Nullable<number>;
  hasStaleSleeves: boolean;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
  /** The shared low / central / high real-return assumption (C8). */
  realAnnualReturnRange: ProjectionRange;
  /** Months to the target at each return; null unless a date exists. The high return is soonest. */
  monthsToFireRange: Nullable<ProjectionRange>;
  /** ISO dates at the high and the low return, present only while the status is `Projected`. */
  earliestDate: Nullable<string>;
  latestDate: Nullable<string>;
  /** How many complete months the median spend and saving are taken over. */
  completeMonths: number;
}
