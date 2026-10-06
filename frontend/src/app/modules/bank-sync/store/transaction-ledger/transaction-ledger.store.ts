import {computed, linkedSignal} from '@angular/core';
import {withUrlSync} from '@lifekit-hq/core';
import {
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withProps,
  withState,
} from '@ngrx/signals';

import {
  type TransactionFilterInputText,
  type TransactionType,
} from '../../../../shared/models/transaction-filters/transaction-filters.model';
import {
  type CommittedInputFilters,
  TransactionFiltersUtils,
} from '../../../../shared/utils/transaction-filters.utils';
import {transactionLedgerComputed} from './transaction-ledger.computed';
import {transactionLedgerEffects, transactionLedgerHooks} from './transaction-ledger.effects';
import {transactionLedgerMethods} from './transaction-ledger.methods';
import {
  initialTransactionLedgerState,
  type TransactionLedgerState,
} from './transaction-ledger.state';

// URL values that do not parse fall back to "no filter" so a stale or hand-edited link never reaches the API.
const TYPE_CODEC = {
  encode: (value: Nullable<TransactionType>) => value ?? '',
  decode: (raw: string) => TransactionFiltersUtils.parseType(raw),
};
const DATE_CODEC = {
  encode: (value: Nullable<string>) => value ?? '',
  decode: (raw: string) => TransactionFiltersUtils.parseDate(raw),
};
const AMOUNT_CODEC = {
  encode: (value: Nullable<number>) => TransactionFiltersUtils.formatAmount(value),
  decode: (raw: string) => TransactionFiltersUtils.parseAmount(raw),
};

// `category` and `type` are deep-link contract (dashboard drill-downs, /income, budgets).
export const TransactionLedgerStore = signalStore(
  withState(initialTransactionLedgerState),
  withUrlSync<TransactionLedgerState>({
    'filters.accountIds': {param: 'account', default: [], codec: 'csv'},
    'filters.categories': {param: 'category', default: [], codec: 'csv'},
    'filters.transactionType': {param: 'type', default: null, codec: TYPE_CODEC},
    'filters.from': {param: 'from', default: null, codec: DATE_CODEC},
    'filters.to': {param: 'to', default: null, codec: DATE_CODEC},
    'filters.minAmount': {param: 'minAmountUsd', default: null, codec: AMOUNT_CODEC},
    'filters.maxAmount': {param: 'maxAmountUsd', default: null, codec: AMOUNT_CODEC},
    'filters.search': {param: 'search', default: ''},
  }),
  withProps(store => {
    const committed = computed(
      () => {
        const {minAmount, maxAmount, search} = store.filters();
        return {minAmount, maxAmount, search};
      },
      {
        equal: (a, b) =>
          a.minAmount === b.minAmount && a.maxAmount === b.maxAmount && a.search === b.search,
      }
    );
    return {
      inputText: linkedSignal<CommittedInputFilters, TransactionFilterInputText>({
        source: committed,
        computation: (filters, previous) =>
          TransactionFiltersUtils.syncInputText(
            filters,
            previous && {committed: previous.source, text: previous.value}
          ),
      }),
    };
  }),
  withMethods(transactionLedgerMethods),
  withComputed(transactionLedgerComputed),
  withMethods(transactionLedgerEffects),
  withHooks({onInit: transactionLedgerHooks})
);
