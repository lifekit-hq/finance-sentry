import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {ALERTS_LOAD_ERROR_MESSAGE} from '../../constants/alert/alert.constants';
import {topAlertComputed} from './top-alert.computed';
import {topAlertEffects, topAlertHooks} from './top-alert.effects';
import {topAlertMethods} from './top-alert.methods';
import {initialTopAlertState} from './top-alert.state';

/** Component-scoped: Home's insight, the newest unread alert and how many more wait in Alerts. */
export const TopAlertStore = signalStore(
  withState(initialTopAlertState),
  withAsyncStatus({defaultErrorMessage: ALERTS_LOAD_ERROR_MESSAGE}),
  withMethods(topAlertMethods),
  withComputed(topAlertComputed),
  withMethods(topAlertEffects),
  withHooks({onInit: topAlertHooks})
);
