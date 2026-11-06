import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withMethods, withState} from '@ngrx/signals';

import {LOAD_PROVIDERS_ERROR} from '../../components/connect-modal/connect-modal.constants';
import {trueLayerPickerEffects} from './truelayer-picker.effects';
import {initialTrueLayerPickerState} from './truelayer-picker.state';

/** Page-scoped: provided on the TrueLayer picker so it tears down with the modal step. */
export const TrueLayerPickerStore = signalStore(
  withState(initialTrueLayerPickerState),
  withAsyncStatus({defaultErrorMessage: LOAD_PROVIDERS_ERROR}),
  withMethods(trueLayerPickerEffects)
);
