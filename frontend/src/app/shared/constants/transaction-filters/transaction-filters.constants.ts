import {type TransactionFilters} from '../../models/transaction-filters/transaction-filters.model';

export const EMPTY_TRANSACTION_FILTERS: TransactionFilters = {
  accountIds: [],
  categories: [],
  transactionType: null,
  from: null,
  to: null,
  minAmount: null,
  maxAmount: null,
  search: '',
};

/** Typing pause before a search or amount edit re-queries the server. */
export const TRANSACTION_FILTER_DEBOUNCE_MS = 300;
