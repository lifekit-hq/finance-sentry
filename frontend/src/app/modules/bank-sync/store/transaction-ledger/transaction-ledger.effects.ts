import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {catchError, debounceTime, filter, forkJoin, of, pipe, switchMap, tap} from 'rxjs';

import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {type MonthlyFlow} from '../../models/dashboard/dashboard.model';
import {
  type GetAllTransactionsParams,
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';
import {PAGE_SIZE} from './transaction-ledger.state';

const MONTH_KEY_PAD = 2;
export const SEARCH_DEBOUNCE_MS = 300;

function currentUtcMonthKey(): string {
  const now = new Date();
  return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(MONTH_KEY_PAD, '0')}`;
}

function sumCurrentMonthOutflow(monthlyFlow: MonthlyFlow[]): number {
  const key = currentUtcMonthKey();
  return monthlyFlow.filter(r => r.month === key).reduce((sum, r) => sum + r.outflowUsd, 0);
}

interface EffectsStore {
  offset: Signal<number>;
  accountId: Signal<Nullable<string>>;
  transactionType: Signal<Nullable<TransactionType>>;
  category: Signal<Nullable<string>>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  search: Signal<string>;
  setAccountId: (accountId: Nullable<string>) => void;
  setTransactionType: (transactionType: Nullable<TransactionType>) => void;
  setCategory: (category: Nullable<string>) => void;
  setDateRange: (from: Nullable<string>, to: Nullable<string>) => void;
  setSearch: (search: string) => void;
  setAccounts: (accounts: TransactionAccountOption[]) => void;
  setLoading: (keepRows?: boolean) => void;
  setTransactions: (
    transactions: GlobalTransactionDto[],
    totalCount: number,
    hasMore: boolean
  ) => void;
  appendTransactions: (
    transactions: GlobalTransactionDto[],
    totalCount: number,
    hasMore: boolean
  ) => void;
  nextPage: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setMonthlyOutflowUsd: (value: number | null, currency?: string) => void;
}

function pageParams(store: EffectsStore, offset: number): GetAllTransactionsParams {
  return {
    offset,
    limit: PAGE_SIZE,
    accountId: store.accountId() ?? undefined,
    transactionType: store.transactionType() ?? undefined,
    category: store.category() ?? undefined,
    from: store.from() ?? undefined,
    to: store.to() ?? undefined,
    search: store.search().trim() || undefined,
  };
}

export function transactionLedgerEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  const fetchPage = rxMethod<'first' | 'next' | 'retry'>(
    pipe(
      tap(page => {
        if (page === 'next') {
          store.nextPage();
        }
        store.setLoading(page !== 'first');
      }),
      switchMap(page =>
        bankSyncService
          .getAllTransactions(pageParams(store, page === 'next' ? store.offset() : 0))
          .pipe(
            tap(res =>
              page === 'next'
                ? store.appendTransactions(res.items, res.totalCount, res.hasMore)
                : store.setTransactions(res.items, res.totalCount, res.hasMore)
            ),
            StoreErrorUtils.catchAndSetError(store)
          )
      )
    )
  );

  const load = (): void => {
    fetchPage('first');
  };

  return {
    load,
    /** Re-issues the failed request with the rows already on screen kept in place. */
    retry: (): void => {
      fetchPage('retry');
    },
    loadSummary: rxMethod<void>(
      pipe(
        switchMap(() =>
          forkJoin({
            dashboard$: bankSyncService.getDashboardData().pipe(catchError(() => of(null))),
            accounts$: bankSyncService.getAccounts().pipe(catchError(() => of(null))),
          }).pipe(
            tap(({dashboard$, accounts$}) => {
              store.setMonthlyOutflowUsd(
                dashboard$ ? sumCurrentMonthOutflow(dashboard$.monthlyFlow) : null,
                dashboard$?.baseCurrency
              );
              store.setAccounts(TransactionGroupUtils.toAccountOptions(accounts$?.accounts ?? []));
            })
          )
        )
      )
    ),
    loadMore: (): void => {
      fetchPage('next');
    },
    /** Follows the `account` query param: a changed value re-queries from the first page. */
    applyAccount: rxMethod<Nullable<string>>(
      pipe(
        filter(accountId => accountId !== store.accountId()),
        tap(accountId => store.setAccountId(accountId)),
        tap(() => load())
      )
    ),
    /** Follows the `type` query param (In / Out): a changed value re-queries from the first page. */
    applyType: rxMethod<Nullable<TransactionType>>(
      pipe(
        filter(transactionType => transactionType !== store.transactionType()),
        tap(transactionType => store.setTransactionType(transactionType)),
        tap(() => load())
      )
    ),
    /**
     * Follows the `category` query param (dashboard Top spendings drill-down): filtered
     * server-side so the whole dataset is searched, not only the loaded pages.
     */
    applyCategory: rxMethod<Nullable<string>>(
      pipe(
        filter(category => category !== store.category()),
        tap(category => store.setCategory(category)),
        tap(() => load())
      )
    ),
    /** Follows the `from`/`to` query params (ISO dates): a changed bound re-queries from the first page. */
    applyDateRange: rxMethod<{from: Nullable<string>; to: Nullable<string>}>(
      pipe(
        filter(({from, to}) => from !== store.from() || to !== store.to()),
        tap(({from, to}) => store.setDateRange(from, to)),
        tap(() => load())
      )
    ),
    /** Follows the search box: debounced, then re-queries from the first page. */
    applySearch: rxMethod<string>(
      pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        filter(search => search.trim() !== store.search().trim()),
        tap(search => store.setSearch(search)),
        tap(() => load())
      )
    ),
  };
}

interface HookStore {
  load: () => void;
  loadSummary: () => void;
}

export function transactionLedgerHooks(store: HookStore): void {
  store.load();
  store.loadSummary();
}
