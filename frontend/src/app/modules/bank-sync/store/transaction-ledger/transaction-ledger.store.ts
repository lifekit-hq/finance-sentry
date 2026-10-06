import {withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {transactionLedgerComputed} from './transaction-ledger.computed';
import {transactionLedgerEffects, transactionLedgerHooks} from './transaction-ledger.effects';
import {transactionLedgerMethods} from './transaction-ledger.methods';
import {
  initialTransactionLedgerState,
  type TransactionLedgerState,
} from './transaction-ledger.state';

// `category` and `type` are deep-link contract (dashboard drill-downs, /income, budgets).
export const TransactionLedgerStore = signalStore(
  withState(initialTransactionLedgerState),
  withUrlSync<TransactionLedgerState>({
    'filters.accountIds': {param: 'account', default: [], codec: 'csv'},
    'filters.categories': {param: 'category', default: [], codec: 'csv'},
    'filters.transactionType': {param: 'type', default: null},
    'filters.from': {param: 'from', default: null},
    'filters.to': {param: 'to', default: null},
    'filters.minAmount': {param: 'minAmountUsd', default: null, codec: 'number'},
    'filters.maxAmount': {param: 'maxAmountUsd', default: null, codec: 'number'},
    'filters.search': {param: 'search', default: ''},
  }),
  withMethods(transactionLedgerMethods),
  withComputed(transactionLedgerComputed),
  withMethods(transactionLedgerEffects),
  withHooks({onInit: transactionLedgerHooks})
);
