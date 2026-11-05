import {type Nullable} from '@lifekit-hq/ui';

/** Mirrors the backend's `CashflowProfileStatus`, written by name. */
export type CashflowProfileStatus = 'Shown' | 'InsufficientHistory' | 'NoCash';

/**
 * Burn, savings rate and runway over the trailing complete months (#454 S1, money-semantics §15).
 * Rates are fractions (0.25 = 25%); the two monthly amounts are in `baseCurrency`; months are "yyyy-MM".
 */
export interface CashflowProfile {
  status: CashflowProfileStatus;
  /** How many complete months the figures cover. */
  completeMonths: number;
  fromMonth: Nullable<string>;
  toMonth: Nullable<string>;
  incomeMonthlyMedian: Nullable<number>;
  burnMonthlyMedian: Nullable<number>;
  /** Pooled over the window, negative while spending exceeds income. Never a per-month ratio. */
  savingsRate: Nullable<number>;
  runwayMonths: Nullable<number>;
  /** Why `runwayMonths` is null; one of `CASHFLOW_PROFILE_REASONS`. */
  runwayReason: Nullable<string>;
  committedShare: Nullable<number>;
  hasStaleSleeves: boolean;
  /** Why the whole profile is withheld (`InsufficientHistory`); one of `CASHFLOW_PROFILE_REASONS`. */
  reason: Nullable<string>;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
  disclosure: string;
}
