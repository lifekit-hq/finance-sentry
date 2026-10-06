import {patchState, type WritableStateSource} from '@ngrx/signals';

import {EMPTY_TRANSACTION_FILTERS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {type TransactionFilters} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
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
    setMonthlyOutflowUsd(value: number | null): void {
      patchState(store, {monthlyOutflowUsd: value});
    },
    /** Any filter change restarts paging from the first page. */
    setFilters(patch: Partial<TransactionFilters>): void {
      patchState(store, state => ({filters: {...state.filters, ...patch}, offset: 0}));
    },
    resetFilters(): void {
      patchState(store, {filters: EMPTY_TRANSACTION_FILTERS, offset: 0});
    },
    /** A first-page load replaces the list, so the previous filter's rows never linger. */
    startFirstPage(): void {
      patchState(store, {offset: 0, transactions: [], totalCount: 0, hasMore: false});
    },
    setAccounts(accounts: TransactionAccountOption[]): void {
      patchState(store, {accounts});
    },
  };
}
