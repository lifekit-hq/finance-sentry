/** The typed reasons the cashflow profile withholds a figure for (backend `CashflowProfileReasons`). */
export const CASHFLOW_PROFILE_REASONS = {
  insufficientMonths: 'INSUFFICIENT_MONTHS',
  noPositiveIncome: 'NO_POSITIVE_INCOME',
  noSnapshot: 'NO_SNAPSHOT',
  noCashSplit: 'NO_CASH_SPLIT',
  noCash: 'NO_CASH',
  noBurn: 'NO_BURN',
} as const;

export const CASHFLOW_PROFILE_ERROR_MESSAGE = 'Failed to load burn, savings rate and runway.';

export const CASHFLOW_PROFILE_EMPTY_MESSAGE = 'Not enough history yet';

/** Mirrors the backend's `Projection.MinCompleteMonths`: below it the profile is withheld. */
export const CASHFLOW_PROFILE_MIN_MONTHS = 3;
