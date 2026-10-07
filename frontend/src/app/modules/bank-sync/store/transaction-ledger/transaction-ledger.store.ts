import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {transactionLedgerComputed} from './transaction-ledger.computed';
import {transactionLedgerEffects, transactionLedgerHooks} from './transaction-ledger.effects';
import {transactionLedgerMethods} from './transaction-ledger.methods';
import {initialTransactionLedgerState} from './transaction-ledger.state';

export const TransactionLedgerStore = signalStore(
  withState(initialTransactionLedgerState),
  withMethods(transactionLedgerMethods),
  withComputed(transactionLedgerComputed),
  withMethods(transactionLedgerEffects),
  withHooks({onInit: transactionLedgerHooks})
);
