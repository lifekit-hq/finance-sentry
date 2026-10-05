import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {commitmentPickerComputed} from './commitment-picker.computed';
import {commitmentPickerEffects, commitmentPickerHooks} from './commitment-picker.effects';
import {commitmentPickerMethods} from './commitment-picker.methods';
import {initialCommitmentPickerState} from './commitment-picker.state';

/** Dialog-scoped: the transaction list behind "Add" lives only while the dialog is open. */
export const CommitmentPickerStore = signalStore(
  withState(initialCommitmentPickerState),
  withMethods(commitmentPickerMethods),
  withComputed(commitmentPickerComputed),
  withMethods(commitmentPickerEffects),
  withHooks({onInit: commitmentPickerHooks})
);
