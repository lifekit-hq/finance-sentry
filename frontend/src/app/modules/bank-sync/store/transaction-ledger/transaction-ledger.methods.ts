import {type Nullable} from '@lifekit-hq/ui';
import {patchState, type WritableStateSource} from '@ngrx/signals';

import {DEFAULT_BASE_CURRENCY} from '../../../../shared/constants/money/money.constants';
import {type MonthlyFlow} from '../../models/dashboard/dashboard.model';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';
import {type LedgerFilter} from './transaction-ledger.computed';
import {
  type AccountOutflow,
  PAGE_SIZE,
  type TransactionLedgerState,
} from './transaction-ledger.state';

export function transactionLedgerMethods(store: WritableStateSource<TransactionLedgerState>) {
  return {
    /** A failed first page drops the previous query's rows; a reload keeps them until it lands. */
    clearRows(): void {
      patchState(store, {
        transactions: [],
        totalCount: 0,
        hasMore: false,
        rowsFilter: null,
        revalidating: false,
      });
    },
    setTransactions(
      transactions: GlobalTransactionDto[],
      totalCount: number,
      hasMore: boolean,
      rowsFilter: LedgerFilter
    ): void {
      patchState(store, {
        transactions,
        totalCount,
        hasMore,
        rowsFilter,
        offset: 0,
        lastSyncedAt: Date.now(),
        revalidating: false,
      });
    },
    setRevalidating(revalidating: boolean): void {
      patchState(store, {revalidating});
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
    setMonthlyFlow(
      monthlyFlow: MonthlyFlow[] | null,
      currency: string = DEFAULT_BASE_CURRENCY
    ): void {
      patchState(store, {monthlyFlow, monthlyOutflowCurrency: currency});
    },
    setAccountOutflow(accountOutflow: Nullable<AccountOutflow>): void {
      patchState(store, {accountOutflow});
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
