import {withAsyncStatus, withNamedAsyncStatus, withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {NULLABLE_STRING_PARAM_CODEC} from '../../../../shared/constants/url-params/url-params.constants';
import {withSessionCache} from '../../../../shared/store/session-cache/with-session-cache.feature';
import {
  ISO_DATE_PARAM_CODEC,
  TRANSACTION_TYPE_PARAM_CODEC,
} from '../../constants/url-params/url-params.constants';
import {transactionLedgerComputed} from './transaction-ledger.computed';
import {transactionLedgerEffects, transactionLedgerHooks} from './transaction-ledger.effects';
import {transactionLedgerMethods} from './transaction-ledger.methods';
import {
  initialTransactionLedgerState,
  LEDGER_SESSION_FIELDS,
  type TransactionLedgerState,
} from './transaction-ledger.state';

/** The kept rows fit only the filter they were loaded for; the URL decides the filter. */
function sameLedgerFilter(kept: Partial<TransactionLedgerState>, current: TransactionLedgerState) {
  return (
    kept.accountId === current.accountId &&
    kept.transactionType === current.transactionType &&
    (kept.categories ?? []).join() === current.categories.join() &&
    kept.from === current.from &&
    kept.to === current.to &&
    kept.search === current.search
  );
}

export const TransactionLedgerStore = signalStore(
  withState(initialTransactionLedgerState),
  withUrlSync({
    accountId: {
      param: 'account',
      default: initialTransactionLedgerState.accountId,
      codec: NULLABLE_STRING_PARAM_CODEC,
    },
    transactionType: {
      param: 'type',
      default: initialTransactionLedgerState.transactionType,
      codec: TRANSACTION_TYPE_PARAM_CODEC,
    },
    categories: {
      param: 'category',
      default: initialTransactionLedgerState.categories,
      codec: 'csv',
    },
    from: {param: 'from', default: initialTransactionLedgerState.from, codec: ISO_DATE_PARAM_CODEC},
    to: {param: 'to', default: initialTransactionLedgerState.to, codec: ISO_DATE_PARAM_CODEC},
    search: {param: 'q', default: initialTransactionLedgerState.search},
  }),
  withSessionCache<TransactionLedgerState>(
    'transaction-ledger',
    LEDGER_SESSION_FIELDS,
    sameLedgerFilter,
    {
      revalidating: true,
    }
  ),
  withAsyncStatus({defaultErrorMessage: 'Failed to load transactions. Please try again.'}),
  // The next page appends under the rows already on screen; `lk-infinite-list` shows its own
  // skeleton rows and inline retry from this status, so it never touches the first-load state.
  withNamedAsyncStatus('append', {defaultErrorMessage: "Couldn't load more transactions."}),
  withMethods(transactionLedgerMethods),
  withComputed(transactionLedgerComputed),
  withMethods(transactionLedgerEffects),
  withHooks({onInit: transactionLedgerHooks})
);
