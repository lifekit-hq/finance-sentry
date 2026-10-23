import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {OutflowUtils} from '../../utils/outflow.utils';
import {type OutflowScope, type ScopedSpending} from './transaction-ledger.state';

interface StateSignals {
  transactions: Signal<GlobalTransactionDto[]>;
  totalCount: Signal<number>;
  hasMore: Signal<boolean>;
  status: Signal<AsyncStatus>;
  spending: Signal<Nullable<ScopedSpending>>;
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
  const spendingScope = computed<OutflowScope>(() => ({
    accountId: store.accountId(),
    from: store.from(),
    to: store.to(),
  }));
  const scopedSpending = computed(() => {
    const spending = store.spending();
    return spending && OutflowUtils.sameScope(spending, spendingScope()) ? spending : null;
  });

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
    /** What the summary line is for: the account and dates the ledger is filtered to. */
    spendingScope,
    /** Out over the selected account and dates, or null while it is not known. */
    outflow: computed(() => {
      const spending = scopedSpending();
      return spending ? {amount: spending.outflow, currency: spending.currency} : null;
    }),
    /** The category that took most of that same outflow; the search and category filters do not narrow it. */
    topCategory: computed(() => {
      const category = scopedSpending()?.topCategory;
      return category ? MerchantCategoryUtils.format(category) : null;
    }),
  };
}
