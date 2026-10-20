import {type Nullable} from '@lifekit-hq/ui';
/** Mirrors the backend's `MonthlyIncomeLensStatus`, written by name. */
export type MonthlyIncomeLensStatus = 'Shown' | 'NoNetWorth';

/**
 * The monthly-income lens (finance-framework P5): the latest net worth drawn at the shared safe
 * withdrawal rate (C8), as a monthly amount in today's money. The rate is a fraction (0.04 = 4%);
 * amounts are in `baseCurrency`.
 */
export interface MonthlyIncomeLens {
  status: MonthlyIncomeLensStatus;
  netWorth: number;
  safeWithdrawalRate: number;
  /** Present only while the status is `Shown`. */
  monthlyIncome: Nullable<number>;
  hasStaleSleeves: boolean;
  /** ISO code the amounts are expressed in: the profile base currency, USD when unset. */
  baseCurrency: string;
}
