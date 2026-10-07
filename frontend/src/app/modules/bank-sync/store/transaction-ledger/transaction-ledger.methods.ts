import {patchState, type WritableStateSource} from '@ngrx/signals';

import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {PAGE_SIZE, type TransactionLedgerState} from './transaction-ledger.state';

export function transactionLedgerMethods(store: WritableStateSource<TransactionLedgerState>) {
  return {
    setLoading(): void {
      patchState(store, {status: 'loading', errorCode: null});
    },
    setTransactions(
      transactions: GlobalTransactionDto[],
      totalCount: number,
      hasMore: boolean
    ): void {
      patchState(store, {transactions, totalCount, hasMore, status: 'idle', errorCode: null});
    },
    setError(errorCode: Nullable<string>): void {
      patchState(store, {status: 'error', errorCode});
    },
    setOffset(offset: number): void {
      patchState(store, {offset});
    },
    nextPage(): void {
      patchState(store, state => ({offset: state.offset + PAGE_SIZE}));
    },
    appendTransactions(
      transactions: GlobalTransactionDto[],
      totalCount: number,
      hasMore: boolean
    ): void {
      patchState(store, state => ({
        transactions: [...state.transactions, ...transactions],
        totalCount,
        hasMore,
        status: 'idle' as AsyncStatus,
        errorCode: null,
      }));
    },
    setMonthlyOutflowUsd(value: number | null, currency: string = DEFAULT_BASE_CURRENCY): void {
      patchState(store, {monthlyOutflowUsd: value, monthlyOutflowCurrency: currency});
    },
    setAccountId(accountId: Nullable<string>): void {
      patchState(store, {accountId, offset: 0});
    },
    setTransactionType(transactionType: Nullable<TransactionType>): void {
      patchState(store, {transactionType, offset: 0});
    },
    setCategory(category: Nullable<string>): void {
      patchState(store, {category, offset: 0});
    },
    setDateRange(from: Nullable<string>, to: Nullable<string>): void {
      patchState(store, {from, to, offset: 0});
    },
    setSearch(search: string): void {
      patchState(store, {search, offset: 0});
    },
    setAccounts(accounts: TransactionAccountOption[]): void {
      patchState(store, {accounts});
    },
  };
}
