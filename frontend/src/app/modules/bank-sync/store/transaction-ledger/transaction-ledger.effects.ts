import {inject, type Signal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  filter,
  forkJoin,
  of,
  pipe,
  switchMap,
  tap,
} from 'rxjs';

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
import {type LedgerFilter} from './transaction-ledger.computed';
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
  transactions: Signal<GlobalTransactionDto[]>;
  offset: Signal<number>;
  accountId: Signal<Nullable<string>>;
  transactionType: Signal<Nullable<TransactionType>>;
  category: Signal<Nullable<string>>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  search: Signal<string>;
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

function sameFilter(a: LedgerFilter, b: LedgerFilter): boolean {
  return (
    a.accountId === b.accountId &&
    a.transactionType === b.transactionType &&
    a.category === b.category &&
    a.from === b.from &&
    a.to === b.to &&
    a.search === b.search
  );
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

  const fetchPage = rxMethod<'first' | 'next'>(
    pipe(
      tap(page => store.setLoading(page === 'next')),
      switchMap(page => {
        const offset = page === 'first' ? 0 : store.offset() + PAGE_SIZE;
        return bankSyncService.getAllTransactions(pageParams(store, offset)).pipe(
          tap(res => {
            if (page === 'next') {
              store.nextPage();
              store.appendTransactions(res.items, res.totalCount, res.hasMore);
            } else {
              store.setTransactions(res.items, res.totalCount, res.hasMore);
            }
          }),
          StoreErrorUtils.catchAndSetError(store)
        );
      })
    )
  );

  const load = (): void => {
    fetchPage('first');
  };

  return {
    load,
    /** Re-issues the failed request with the rows already on screen kept in place. */
    retry: (): void => {
      fetchPage(store.transactions().length > 0 ? 'next' : 'first');
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
    /** Follows the search box: debounced, then written to the `q` query param by `withUrlSync`. */
    applySearch: rxMethod<string>(
      pipe(
        debounceTime(SEARCH_DEBOUNCE_MS),
        filter(search => search.trim() !== store.search().trim()),
        tap(search => store.setSearch(search))
      )
    ),
    /**
     * Loads the ledger for the filters (synced from the query params by `withUrlSync`), again on
     * every change. The first emission is the page's first load.
     */
    watchFilter: rxMethod<LedgerFilter>(
      pipe(
        distinctUntilChanged(sameFilter),
        tap(() => load())
      )
    ),
  };
}

interface HookStore {
  filter: Signal<LedgerFilter>;
  watchFilter: (filter: Signal<LedgerFilter>) => void;
  loadSummary: () => void;
}

export function transactionLedgerHooks(store: HookStore): void {
  store.watchFilter(store.filter);
  store.loadSummary();
}
