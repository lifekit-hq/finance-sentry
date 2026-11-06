import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withMethods, withState} from '@ngrx/signals';

import {ibkrConnectComputed} from './ibkr-connect.computed';
import {ibkrConnectEffects} from './ibkr-connect.effects';
import {ibkrConnectMethods} from './ibkr-connect.methods';
import {DEFAULT_VALIDATION_ERROR, initialIbkrConnectState} from './ibkr-connect.state';

/** Page-scoped: provided on the IBKR form so it tears down with the modal step. */
export const IbkrConnectStore = signalStore(
  withState(initialIbkrConnectState),
  withAsyncStatus({defaultErrorMessage: DEFAULT_VALIDATION_ERROR}),
  withMethods(ibkrConnectMethods),
  withComputed(ibkrConnectComputed),
  withMethods(ibkrConnectEffects)
);
