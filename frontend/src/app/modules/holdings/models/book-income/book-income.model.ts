import {type Nullable} from '@lifekit-hq/ui';

/** Income over one window in USD: what was paid, the tax withheld from it, and what reached the account. */
export interface IncomeWindow {
  gross: number;
  withholding: number;
  net: number;
}

/** What an income row was (the server's `IncomeReceiptKind` enum): 0 = dividend, 1 = interest, 2 = staking. */
export type IncomeKind = 0 | 1 | 2;

/** One holding's income from `GET /radar/book-performance/income`. */
export interface HoldingIncome {
  symbol: string;
  kinds: IncomeKind[];
  ttm: IncomeWindow;
  ytd: IncomeWindow;
  /** Stored dividend yield times the position's value; null without a yield. */
  forwardAnnualUsd: Nullable<number>;
  /** Trailing-12-month net income over the verified cost basis; a fraction, null without a basis. */
  yieldOnCost: Nullable<number>;
  nextExDividend: Nullable<string>;
}

/** The book's income received: dividends, interest and staking, with the forward estimate and yield on cost. */
export interface BookIncome {
  asOf: string;
  ttm: IncomeWindow;
  ytd: IncomeWindow;
  forwardAnnualUsd: Nullable<number>;
  yieldOnCost: Nullable<number>;
  /** Income rows left out for want of a USD value. */
  unvaluedReceipts: number;
  holdings: HoldingIncome[];
}
