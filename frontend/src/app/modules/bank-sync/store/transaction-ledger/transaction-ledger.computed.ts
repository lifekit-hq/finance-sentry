import {computed, inject, type Signal} from '@angular/core';
import {ErrorMessageService} from '@lifekit-hq/core';

import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
} from '../../models/transaction/transaction.model';

interface StateSignals {
  transactions: Signal<GlobalTransactionDto[]>;
  totalCount: Signal<number>;
  hasMore: Signal<boolean>;
  status: Signal<AsyncStatus>;
  errorCode: Signal<Nullable<string>>;
  monthlyOutflowUsd: Signal<number | null>;
  accountId: Signal<Nullable<string>>;
  search: Signal<string>;
  accounts: Signal<TransactionAccountOption[]>;
}

const DEFAULT_ERROR = 'Failed to load transactions. Please try again.';

export function transactionLedgerComputed(store: StateSignals) {
  const errorMessages = inject(ErrorMessageService);

  return {
    isLoading: computed(() => store.status() === 'loading'),
    isEmpty: computed(() => store.status() === 'idle' && store.transactions().length === 0),
    hasActiveFilter: computed(() => store.accountId() !== null || store.search().trim() !== ''),
    errorMessage: computed(() => {
      if (store.status() !== 'error') {
        return '';
      }
      return errorMessages.resolve(store.errorCode()) ?? DEFAULT_ERROR;
    }),
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
