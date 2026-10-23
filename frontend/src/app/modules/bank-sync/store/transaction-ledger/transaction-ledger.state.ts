import {type Nullable} from '@lifekit-hq/ui';

import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {type MonthlyFlow} from '../../models/dashboard/dashboard.model';
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

export interface AccountOutflow extends OutflowScope {
  amount: number;
  currency: string;
}

export interface TransactionLedgerState {
  transactions: GlobalTransactionDto[];
  totalCount: number;
  hasMore: boolean;
  offset: number;
  /** Epoch ms of the last successful page load; drives the offline "last synced" notice. */
  lastSyncedAt: Nullable<number>;
  /** The dashboard's whole-month flow history; null until it has loaded (or when it failed). */
  monthlyFlow: MonthlyFlow[] | null;
  /** Currency of the `…Usd` figures in `monthlyFlow` (the profile's base currency). */
  monthlyOutflowCurrency: string;
  /** Outflow of one account over a date range: month flow is per user, so an account is summed from its debit rows. */
  accountOutflow: Nullable<AccountOutflow>;
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
  monthlyFlow: null,
  monthlyOutflowCurrency: DEFAULT_BASE_CURRENCY,
  accountOutflow: null,
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
  'monthlyFlow',
  'monthlyOutflowCurrency',
  'accountOutflow',
  'accounts',
  'rowsFilter',
] as const satisfies readonly (keyof TransactionLedgerState)[];
