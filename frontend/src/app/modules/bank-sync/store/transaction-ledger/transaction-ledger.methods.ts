import {patchState, type WritableStateSource} from '@ngrx/signals';

import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';
import {PAGE_SIZE, type TransactionLedgerState} from './transaction-ledger.state';

export function transactionLedgerMethods(store: WritableStateSource<TransactionLedgerState>) {
  return {
    /** `keepRows` holds the rows on screen while a retry or the next page loads. */
    setLoading(keepRows = false): void {
      patchState(store, {
        status: 'loading',
        errorCode: null,
        ...(keepRows ? {} : {transactions: [], totalCount: 0, hasMore: false}),
      });
    },
    setTransactions(
      transactions: GlobalTransactionDto[],
      totalCount: number,
      hasMore: boolean
    ): void {
      patchState(store, {
        transactions,
        totalCount,
        hasMore,
        offset: 0,
        status: 'idle',
        errorCode: null,
        lastSyncedAt: Date.now(),
      });
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
        lastSyncedAt: Date.now(),
      }));
    },
    setMonthlyOutflowUsd(value: number | null, currency: string = DEFAULT_BASE_CURRENCY): void {
      patchState(store, {monthlyOutflowUsd: value, monthlyOutflowCurrency: currency});
    },
    /** A blank box reads as no search, so it never reaches the `q` query param. */
    setSearch(search: string): void {
      patchState(store, {search: search.trim() === '' ? '' : search, offset: 0});
    },
    setAccounts(accounts: TransactionAccountOption[]): void {
      patchState(store, {accounts});
    },
  };
}
