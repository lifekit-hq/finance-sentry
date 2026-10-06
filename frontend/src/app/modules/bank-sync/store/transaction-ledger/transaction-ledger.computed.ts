import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {type TransactionFilters} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {TransactionFiltersUtils} from '../../../../shared/utils/transaction-filters.utils';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';
import {TransactionGroupUtils} from '../../utils/transaction-group.utils';

interface StateSignals {
  transactions: Signal<GlobalTransactionDto[]>;
  totalCount: Signal<number>;
  hasMore: Signal<boolean>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  monthlyOutflowUsd: Signal<number | null>;
  filters: Signal<TransactionFilters>;
  accounts: Signal<TransactionAccountOption[]>;
}

const DEFAULT_ERROR = 'Failed to load transactions. Please try again.';

export function transactionLedgerComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isLoading: computed(() => store.status() === 'loading'),
    isEmpty: computed(() => store.status() === 'idle' && store.transactions().length === 0),
    hasActiveFilter: computed(() => TransactionFiltersUtils.isActive(store.filters())),
    accountOptions: computed(() =>
      store.accounts().map(account => ({value: account.accountId, label: account.label}))
    ),
    dateRange: computed(() => ({from: store.filters().from, to: store.filters().to})),
    errorMessage: computed(() => {
      if (store.status() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.errorCode()) ?? DEFAULT_ERROR;
    }),
    dayGroups: computed(() => TransactionGroupUtils.groupByDay(store.transactions())),
    monthlyOutflow: computed(() => store.monthlyOutflowUsd()),
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
