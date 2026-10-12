import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withHooks, withMethods, withState} from '@ngrx/signals';

import {commitmentPickerEffects, commitmentPickerHooks} from './commitment-picker.effects';
import {commitmentPickerMethods} from './commitment-picker.methods';
import {initialCommitmentPickerState} from './commitment-picker.state';

/** Dialog-scoped: the transaction list behind "Add" lives only while the dialog is open. */
export const CommitmentPickerStore = signalStore(
  withState(initialCommitmentPickerState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load transactions.'}),
  withMethods(commitmentPickerMethods),
  withMethods(commitmentPickerEffects),
  withHooks({onInit: commitmentPickerHooks})
);
