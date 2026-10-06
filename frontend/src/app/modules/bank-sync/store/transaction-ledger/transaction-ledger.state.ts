import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';

export interface TransactionLedgerState {
  transactions: GlobalTransactionDto[];
  totalCount: number;
  hasMore: boolean;
  offset: number;
  status: AsyncStatus;
  errorCode: Nullable<string>;
  monthlyOutflowUsd: number | null;
  /** Server-side filter: a single account, or null for all. */
  accountId: Nullable<string>;
  /** Server-side filter: credits (In), debits (Out), or null for all. */
  transactionType: Nullable<TransactionType>;
  /** Server-side filter: inclusive `YYYY-MM-DD` date bounds, or null for open-ended. */
  from: Nullable<string>;
  to: Nullable<string>;
  /** Server-side free-text filter (description / merchant). */
  search: string;
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
  accountId: null,
  transactionType: null,
  from: null,
  to: null,
  search: '',
  accounts: [],
};
