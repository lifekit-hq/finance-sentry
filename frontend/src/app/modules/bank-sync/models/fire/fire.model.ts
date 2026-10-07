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
}
