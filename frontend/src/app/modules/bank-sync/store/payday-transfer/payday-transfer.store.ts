import {withAsyncStatus, withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {paydayTransferComputed} from './payday-transfer.computed';
import {paydayTransferEffects, paydayTransferHooks} from './payday-transfer.effects';
import {paydayTransferMethods} from './payday-transfer.methods';
import {initialPaydayTransferState} from './payday-transfer.state';

export const PaydayTransferStore = signalStore(
  withState(initialPaydayTransferState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the payday transfer.'}),
  withNamedAsyncStatus('save', {defaultErrorMessage: 'Failed to save the settings.'}),
  withMethods(paydayTransferMethods),
  withComputed(paydayTransferComputed),
  withMethods(paydayTransferEffects),
  withHooks({onInit: paydayTransferHooks})
);
