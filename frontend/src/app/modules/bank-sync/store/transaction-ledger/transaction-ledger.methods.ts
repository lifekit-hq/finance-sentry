import {patchState, type WritableStateSource} from '@ngrx/signals';

import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';
import {PAGE_SIZE, type TransactionLedgerState} from './transaction-ledger.state';

export function transactionLedgerMethods(store: WritableStateSource<TransactionLedgerState>) {
  return {
    /** A failed first page drops the previous query's rows; a reload keeps them until it lands. */
    clearRows(): void {
      patchState(store, {transactions: [], totalCount: 0, hasMore: false, revalidating: false});
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
        lastSyncedAt: Date.now(),
        revalidating: false,
      });
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
