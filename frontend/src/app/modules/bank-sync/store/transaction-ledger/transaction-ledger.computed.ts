import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';

interface StateSignals {
  transactions: Signal<GlobalTransactionDto[]>;
  totalCount: Signal<number>;
  hasMore: Signal<boolean>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  monthlyOutflowUsd: Signal<number | null>;
  monthlyOutflowCurrency: Signal<string>;
  accountId: Signal<Nullable<string>>;
  transactionType: Signal<Nullable<TransactionType>>;
  search: Signal<string>;
  category: Signal<Nullable<string>>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  accounts: Signal<TransactionAccountOption[]>;
}

/** What the server is asked to filter by: a change re-queries the ledger from the first page. */
export interface LedgerFilter {
  accountId: Nullable<string>;
  transactionType: Nullable<TransactionType>;
  category: Nullable<string>;
  from: Nullable<string>;
  to: Nullable<string>;
  search: string;
}

const DEFAULT_ERROR = 'Failed to load transactions. Please try again.';

export function transactionLedgerComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    filter: computed<LedgerFilter>(() => ({
      accountId: store.accountId(),
      transactionType: store.transactionType(),
      category: store.category(),
      from: store.from(),
      to: store.to(),
      search: store.search().trim(),
    })),
    isLoading: computed(() => store.status() === 'loading'),
    isEmpty: computed(() => store.status() === 'idle' && store.transactions().length === 0),
    hasActiveFilter: computed(
      () =>
        store.accountId() !== null ||
        store.transactionType() !== null ||
        store.search().trim() !== ''
    ),
    errorMessage: computed(() => {
      if (store.status() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.errorCode()) ?? DEFAULT_ERROR;
    }),
    monthlyOutflow: computed(() => store.monthlyOutflowUsd()),
    monthlyOutflowCurrency: computed(() => store.monthlyOutflowCurrency()),
    topCategory: computed(() => {
      const counts: Record<string, number> = {};
      for (const t of store.transactions()) {
        const cat = t.merchantCategory ?? 'Uncategorized';
        counts[cat] = (counts[cat] ?? 0) + 1;
      }
      const entries = Object.entries(counts);
      if (entries.length === 0) {
        return null;
      }
      return MerchantCategoryUtils.format(entries.reduce((a, b) => (a[1] >= b[1] ? a : b))[0]);
    }),
  };
}
