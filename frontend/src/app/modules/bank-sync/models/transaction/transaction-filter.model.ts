import {type TransactionType} from './transaction.model';

/** The ledger filters the filter sheet edits; each round-trips through a URL query param. */
export interface TransactionFilterSelection {
  from: Nullable<string>;
  to: Nullable<string>;
  type: Nullable<TransactionType>;
  categories: string[];
}

export const EMPTY_TRANSACTION_FILTER: TransactionFilterSelection = {
  from: null,
  to: null,
  type: null,
  categories: [],
};

/** An applied filter shown as a removable chip under the search. */
export interface AppliedTransactionFilter {
  id: string;
  label: string;
  remove: () => void;
}
