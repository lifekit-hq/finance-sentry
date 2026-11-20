import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {riskPictureComputed} from './risk-picture.computed';
import {riskPictureEffects, riskPictureHooks} from './risk-picture.effects';
import {riskPictureMethods} from './risk-picture.methods';
import {initialRiskPictureState} from './risk-picture.state';

/** Component-scoped: the investments tab's risk panel. */
export const RiskPictureStore = signalStore(
  withState(initialRiskPictureState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the risk picture.'}),
  withMethods(riskPictureMethods),
  withComputed(riskPictureComputed),
  withMethods(riskPictureEffects),
  withHooks(riskPictureHooks)
);
