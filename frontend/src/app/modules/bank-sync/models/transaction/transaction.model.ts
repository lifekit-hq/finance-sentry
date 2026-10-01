import {type PagedRequest, type PagedResponse} from '../../../../shared/models/api/api.model';
import {type Timestamped} from '../../../../shared/models/timestamped/timestamped.model';

export type TransactionType = 'debit' | 'credit';

export interface GlobalTransactionDto extends Timestamped {
  transactionId: string;
  accountId: string;
  bankName: string;
  currency: string;
  amount: number;
  amountUsd: number;
  date: string;
  postedDate: Nullable<string>;
  description: string;
  transactionType: Nullable<TransactionType>;
  merchantCategory: Nullable<string>;
  isPending: boolean;
}

export type GlobalTransactionsResponse = PagedResponse<GlobalTransactionDto>;

export interface GetAllTransactionsParams extends PagedRequest {
  from?: string;
  to?: string;
  transactionType?: 'credit' | 'debit';
  accountId?: string;
  search?: string;
}

/** One selectable account in the ledger's Account filter. */
export interface TransactionAccountOption {
  accountId: string;
  label: string;
}

/** Transactions sharing one calendar day, newest day first. */
export interface TransactionDayGroup {
  /** Calendar day as `YYYY-MM-DD`. */
  dayKey: string;
  label: string;
  items: GlobalTransactionDto[];
}
