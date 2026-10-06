import {inject, type Signal, type WritableSignal} from '@angular/core';
import {rxMethod} from '@ngrx/signals/rxjs-interop';
import {
  catchError,
  debounceTime,
  filter,
  forkJoin,
  groupBy,
  map,
  mergeMap,
  of,
  pipe,
  switchMap,
  tap,
} from 'rxjs';

import {TRANSACTION_FILTER_DEBOUNCE_MS} from '../../../../shared/constants/transaction-filters/transaction-filters.constants';
import {
  type TransactionFilterInputText,
  type TransactionFilters,
} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {StoreErrorUtils} from '../../../../shared/utils/store-error.utils';
import {TransactionFiltersUtils} from '../../../../shared/utils/transaction-filters.utils';
import {type MonthlyFlow} from '../../models/dashboard/dashboard.model';
import {
  type GetAllTransactionsParams,
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';
import {BankSyncService} from '../../services/bank-sync.service';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';
import {PAGE_SIZE} from './transaction-ledger.state';

const MONTH_KEY_PAD = 2;
const EMPTY_INPUT_TEXT: TransactionFilterInputText = {minAmount: '', maxAmount: '', search: ''};

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
  filters: Signal<TransactionFilters>;
  inputText: WritableSignal<TransactionFilterInputText>;
  setFilters: (patch: Partial<TransactionFilters>) => void;
  resetFilters: () => void;
  startFirstPage: () => void;
  setAccounts: (accounts: TransactionAccountOption[]) => void;
  setLoading: () => void;
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
  setMonthlyOutflowUsd: (value: number | null) => void;
}

/** Maps the filters onto `GET accounts/transactions`; inactive dimensions are left out. */
export function toTransactionParams(
  filters: TransactionFilters,
  offset: number
): GetAllTransactionsParams {
  const params: GetAllTransactionsParams = {offset, limit: PAGE_SIZE};
  if (filters.accountIds.length > 0) {
    params.accountId = filters.accountIds;
  }
  if (filters.categories.length > 0) {
    params.category = filters.categories;
  }
  if (filters.transactionType !== null) {
    params.transactionType = filters.transactionType;
  }
  if (filters.from !== null) {
    params.from = filters.from;
  }
  if (filters.to !== null) {
    params.to = filters.to;
  }
  if (filters.minAmount !== null && Number.isFinite(filters.minAmount)) {
    params.minAmountUsd = filters.minAmount;
  }
  if (filters.maxAmount !== null && Number.isFinite(filters.maxAmount)) {
    params.maxAmountUsd = filters.maxAmount;
  }
  const search = filters.search.trim();
  if (search !== '') {
    params.search = search;
  }
  return params;
}

export function transactionLedgerEffects(store: EffectsStore) {
  const bankSyncService = inject(BankSyncService);

  const fetchPage = rxMethod<'first' | 'next'>(
    pipe(
      tap(page => {
        if (page === 'next') {
          store.nextPage();
        } else {
          store.startFirstPage();
        }
        store.setLoading();
      }),
      switchMap(page =>
        bankSyncService
          .getAllTransactions(toTransactionParams(store.filters(), store.offset()))
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
    loadSummary: rxMethod<void>(
      pipe(
        switchMap(() =>
          forkJoin({
            dashboard$: bankSyncService.getDashboardData().pipe(catchError(() => of(null))),
            accounts$: bankSyncService.getAccounts().pipe(catchError(() => of(null))),
          }).pipe(
            tap(({dashboard$, accounts$}) => {
              store.setMonthlyOutflowUsd(
                dashboard$ ? sumCurrentMonthOutflow(dashboard$.monthlyFlow) : null
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
    /** Re-queries from the first page whenever the filters change (URL, controls or reset). */
    reloadOnFilterChange: rxMethod<TransactionFilters>(pipe(tap(() => load()))),
    /** Clearing also drops typed text still waiting on its debounce, so it cannot re-apply. */
    clearFilters: (): void => {
      store.resetFilters();
      store.inputText.set(EMPTY_INPUT_TEXT);
    },
    /** Search box: waits for typing to pause, then applies the term. */
    applySearch: rxMethod<string>(
      pipe(
        tap(search => store.inputText.update(text => ({...text, search}))),
        debounceTime(TRANSACTION_FILTER_DEBOUNCE_MS),
        filter(search => search === store.inputText().search),
        filter(search => search.trim() !== store.filters().search.trim()),
        tap(search => store.setFilters({search}))
      )
    ),
    /** Amount inputs (USD): wait for typing to pause, then apply the parsed bound. */
    applyAmount: rxMethod<{bound: 'minAmount' | 'maxAmount'; raw: Nullable<string>}>(
      pipe(
        tap(({bound, raw}) => store.inputText.update(text => ({...text, [bound]: raw ?? ''}))),
        // Each bound debounces on its own, so typing max never swallows a pending min.
        groupBy(({bound}) => bound),
        mergeMap(group$ => group$.pipe(debounceTime(TRANSACTION_FILTER_DEBOUNCE_MS))),
        filter(({bound, raw}) => (raw ?? '') === store.inputText()[bound]),
        map(({bound, raw}) => ({bound, value: TransactionFiltersUtils.parseAmount(raw)})),
        filter(({bound, value}) => value !== store.filters()[bound]),
        tap(({bound, value}) => store.setFilters({[bound]: value}))
      )
    ),
  };
}

interface HookStore {
  filters: Signal<TransactionFilters>;
  reloadOnFilterChange: (filters: Signal<TransactionFilters>) => void;
  loadSummary: () => void;
}

export function transactionLedgerHooks(store: HookStore): void {
  // The first emission is the initial load (filters already hydrated from the URL).
  store.reloadOnFilterChange(store.filters);
  store.loadSummary();
}
