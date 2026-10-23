import {type Nullable} from '@lifekit-hq/ui';

import {type SpendingSummary} from '../../models/dashboard/spending-summary.model';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {type LedgerFilter} from './transaction-ledger.computed';

export interface OutflowScope {
  accountId: Nullable<string>;
  from: Nullable<string>;
  to: Nullable<string>;
}

export interface ScopedSpending extends OutflowScope, SpendingSummary {}

export interface TransactionLedgerState {
  transactions: GlobalTransactionDto[];
  totalCount: number;
  hasMore: boolean;
  offset: number;
  /** Epoch ms of the last successful page load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  /** The summary line's outflow and top category, read for the account and dates in `scope`. */
  spending: Nullable<ScopedSpending>;
  /** Server-side filter: a single account, or null for all. */
  accountId: Nullable<string>;
  /** Server-side filter: credits (In), debits (Out), or null for all. */
  transactionType: Nullable<TransactionType>;
  /** Server-side filter: canonical category keys (any match); empty for all. */
  categories: string[];
  /** Server-side filter: inclusive `YYYY-MM-DD` date bounds, or null for open-ended. */
  from: Nullable<string>;
  to: Nullable<string>;
  /** Server-side free-text filter (description / merchant). */
  search: string;
  accounts: TransactionAccountOption[];
  /** The filter the rows on screen were loaded for; null until a first page has landed. */
  rowsFilter: Nullable<LedgerFilter>;
  /** The rows were restored from the session cache and their first reload has not landed yet. */
  revalidating: boolean;
}

export const PAGE_SIZE = 50;

export const initialTransactionLedgerState: TransactionLedgerState = {
  transactions: [],
  totalCount: 0,
  hasMore: false,
  offset: 0,
  lastSyncedAt: null,
  spending: null,
  accountId: null,
  transactionType: null,
  categories: [],
  from: null,
  to: null,
  search: '',
  accounts: [],
  rowsFilter: null,
  revalidating: false,
};

/** What a revisit restores: the rows and summary of the last visit, with the filter they were loaded for. The URL alone decides the current filter. */
export const LEDGER_SESSION_FIELDS = [
  'transactions',
  'totalCount',
  'hasMore',
  'offset',
  'lastSyncedAt',
  'spending',
  'accounts',
  'rowsFilter',
] as const satisfies readonly (keyof TransactionLedgerState)[];
