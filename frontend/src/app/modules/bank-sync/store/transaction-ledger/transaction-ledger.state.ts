import {EMPTY_TRANSACTION_FILTERS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {type TransactionFilters} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';

export interface TransactionLedgerState {
  transactions: GlobalTransactionDto[];
  /** Server total over the filtered set. */
  totalCount: number;
  hasMore: boolean;
  offset: number;
  status: AsyncStatus;
  errorCode: Nullable<string>;
  monthlyOutflowUsd: number | null;
  /** Server-side filters, synced to the query string; amount bounds are USD. */
  filters: TransactionFilters;
  accounts: TransactionAccountOption[];
}

export const PAGE_SIZE = 50;

export const initialTransactionLedgerState: TransactionLedgerState = {
  transactions: [],
  totalCount: 0,
  hasMore: false,
  offset: 0,
  status: 'idle',
  errorCode: null,
  monthlyOutflowUsd: null,
  filters: EMPTY_TRANSACTION_FILTERS,
  accounts: [],
};
