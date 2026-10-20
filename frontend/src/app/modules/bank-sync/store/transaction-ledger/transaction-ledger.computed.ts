import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

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
  monthlyOutflowUsd: Signal<number | null>;
  monthlyOutflowCurrency: Signal<string>;
  accountId: Signal<Nullable<string>>;
  transactionType: Signal<Nullable<TransactionType>>;
  search: Signal<string>;
  categories: Signal<string[]>;
  from: Signal<Nullable<string>>;
  to: Signal<Nullable<string>>;
  accounts: Signal<TransactionAccountOption[]>;
}

/** What the server is asked to filter by: a change re-queries the ledger from the first page. */
export interface LedgerFilter {
  accountId: Nullable<string>;
  transactionType: Nullable<TransactionType>;
  categories: string[];
  from: Nullable<string>;
  to: Nullable<string>;
  search: string;
}

export function transactionLedgerComputed(store: StateSignals) {
  return {
    filter: computed<LedgerFilter>(() => ({
      accountId: store.accountId(),
      transactionType: store.transactionType(),
      categories: store.categories(),
      from: store.from(),
      to: store.to(),
      search: store.search().trim(),
    })),
    isEmpty: computed(() => store.status() === 'success' && store.transactions().length === 0),
    hasActiveFilter: computed(
      () =>
        store.accountId() !== null ||
        store.transactionType() !== null ||
        store.search().trim() !== ''
    ),
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
