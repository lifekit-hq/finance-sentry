import {computed, type Signal} from '@angular/core';
import {type AsyncStatus} from '@lifekit-hq/core';
import {type Nullable} from '@lifekit-hq/ui';

import {MerchantCategoryUtils} from '../../../../shared/utils/merchant-category.utils';
import {type MonthlyFlow} from '../../models/dashboard/dashboard.model';
import {
  type GlobalTransactionDto,
  type TransactionAccountOption,
  type TransactionType,
} from '../../models/transaction/transaction.model';
import {OutflowUtils} from '../../utils/outflow.utils';
import {type AccountOutflow, type OutflowScope} from './transaction-ledger.state';

interface StateSignals {
  transactions: Signal<GlobalTransactionDto[]>;
  totalCount: Signal<number>;
  hasMore: Signal<boolean>;
  status: Signal<AsyncStatus>;
  monthlyFlow: Signal<MonthlyFlow[] | null>;
  monthlyOutflowCurrency: Signal<string>;
  accountOutflow: Signal<Nullable<AccountOutflow>>;
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
    /** What the outflow figure is for: the account and dates the ledger is filtered to. */
    outflowScope: computed<OutflowScope>(() => ({
      accountId: store.accountId(),
      from: store.from(),
      to: store.to(),
    })),
    /** Out over the selected period and account, or null while it is not known. */
    outflow: computed(() => {
      const scope = {accountId: store.accountId(), from: store.from(), to: store.to()};
      const account = store.accountOutflow();
      if (!OutflowUtils.usesMonthFlow(scope)) {
        return account && OutflowUtils.sameScope(account, scope)
          ? {amount: account.amount, currency: account.currency}
          : null;
      }
      const flow = store.monthlyFlow();
      return flow
        ? {
            amount: OutflowUtils.sumMonths(flow, scope.from, scope.to),
            currency: store.monthlyOutflowCurrency(),
          }
        : null;
    }),
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
