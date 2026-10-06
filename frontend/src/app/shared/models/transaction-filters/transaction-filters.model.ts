/** Direction of a transaction: money out (`debit`) or in (`credit`). */
export type TransactionType = 'debit' | 'credit';

/**
 * One filter vocabulary for every transaction list - the global ledger and the per-account page.
 * Empty arrays, `null` bounds and an empty search mean "no filter on that dimension".
 */
export interface TransactionFilters {
  /** Accounts to include. The per-account page is path-scoped and leaves this empty. */
  accountIds: string[];
  /** Canonical category keys (`FOOD_AND_DRINK`, ...). */
  categories: string[];
  transactionType: Nullable<TransactionType>;
  /** Inclusive `yyyy-MM-dd` bounds. */
  from: Nullable<string>;
  to: Nullable<string>;
  /** Amount magnitude bounds: USD-normalised on the global ledger, native on the per-account page. */
  minAmount: Nullable<number>;
  maxAmount: Nullable<number>;
  /** Free text over description and merchant. */
  search: string;
}
