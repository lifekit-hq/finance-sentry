import {inject, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {
  catchError,
  debounceTime,
  distinctUntilChanged,
  exhaustMap,
  filter,
  forkJoin,
  of,
  pipe,
  Subject,
  switchMap,
  takeUntil,
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
import {LedgerFilterUtils} from '../../utils/ledger-filter.utils';
import {MonthKeyUtils} from '../../utils/month-key.utils';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';
import {type LedgerFilter} from './transaction-ledger.computed';
import {PAGE_SIZE} from './transaction-ledger.state';

export const SEARCH_DEBOUNCE_MS = 300;

function sumCurrentMonthOutflow(monthlyFlow: MonthlyFlow[]): number {
  const key = MonthKeyUtils.currentUtc();
  return monthlyFlow.filter(r => r.month === key).reduce((sum, r) => sum + r.outflowUsd, 0);
}

interface EffectsStore {
  transactions: Signal<GlobalTransactionDto[]>;
  offset: Signal<number>;
  status: Signal<AsyncStatus>;
  filter: Signal<LedgerFilter>;
  rowsFilter: Signal<Nullable<LedgerFilter>>;
  accountId: Signal<Nullable<string>>;
  transactionType: Signal<Nullable<TransactionType>>;
  categories: Signal<string[]>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  search: Signal<string>;
  setSearch: (search: string) => void;
  setAccounts: (accounts: TransactionAccountOption[]) => void;
  clearRows: () => void;
  setLoading: () => void;
  setSuccess: () => void;
  setTransactions: (
    transactions: GlobalTransactionDto[],
    totalCount: number,
    hasMore: boolean,
    rowsFilter: LedgerFilter
  ) => void;
  setRevalidating: (revalidating: boolean) => void;
  appendTransactions: (
    transactions: GlobalTransactionDto[],
    totalCount: number,
    hasMore: boolean
  ) => void;
  nextPage: () => void;
  setError: (errorCode: Nullable<string>) => void;
  setAppendLoading: () => void;
  setAppendSuccess: () => void;
  setAppendError: (errorCode: Nullable<string>) => void;
  setAppendIdle: () => void;
  setMonthlyOutflowUsd: (value: number | null, currency?: string) => void;
}

function pageParams(store: EffectsStore, offset: number): GetAllTransactionsParams {
  return {
    offset,
    limit: PAGE_SIZE,
    accountId: store.accountId() ?? undefined,
    transactionType: store.transactionType() ?? undefined,
    category: store.categories().length > 0 ? store.categories() : undefined,
    from: store.from() ?? undefined,
    to: store.to() ?? undefined,
    search: store.search().trim() || undefined,
  };
}

export function transactionLedgerEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  /**
   * The first page replaces the rows when it lands; until then the previous rows stay on screen
   * (the page keeps them, dimmed) so a filter change or refresh never blanks the list.
   */
  // A first page (a filter change, a refresh) drops any next page still in flight: it belongs to
  // the rows being replaced.
  const firstPageStarted$ = new Subject<void>();

  const rowsMatchFilter = (): boolean => LedgerFilterUtils.same(store.rowsFilter(), store.filter());

  const fetchFirstPage = rxMethod<void>(
    pipe(
      tap(() => {
        firstPageStarted$.next();
        store.setAppendIdle();
        if (!rowsMatchFilter()) {
          store.setRevalidating(false);
        }
        store.setLoading();
      }),
      switchMap(() => {
        const requested = store.filter();
        return bankSyncService.getAllTransactions(pageParams(store, 0)).pipe(
          tap(res => {
            store.setTransactions(res.items, res.totalCount, res.hasMore, requested);
            store.setAppendSuccess();
            store.setSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => {
              if (!rowsMatchFilter()) {
                store.clearRows();
              }
              store.setError(code);
            },
          })
        );
      })
    )
  );

  /** Appends the next page under the rows on screen (the infinite list's paging contract). */
  const fetchNextPage = rxMethod<void>(
    pipe(
      filter(() => store.status() !== 'loading' && rowsMatchFilter()),
      tap(() => store.setAppendLoading()),
      exhaustMap(() =>
        bankSyncService.getAllTransactions(pageParams(store, store.offset() + PAGE_SIZE)).pipe(
          tap(res => {
            store.nextPage();
            store.appendTransactions(res.items, res.totalCount, res.hasMore);
            store.setAppendSuccess();
          }),
          StoreErrorUtils.catchAndSetError({
            setError: (code: Nullable<string>) => store.setAppendError(code),
          }),
          takeUntil(firstPageStarted$)
        )
      )
    )
  );

  const load = (): void => {
    fetchFirstPage();
  };

  return {
    load,
    /** Re-issues the failed first page. */
    retry: load,
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
    /** Asked by the infinite list when its end is in reach, and by its Try again. */
    loadMore: (): void => {
      fetchNextPage();
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
        distinctUntilChanged((a, b) => LedgerFilterUtils.same(a, b)),
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
