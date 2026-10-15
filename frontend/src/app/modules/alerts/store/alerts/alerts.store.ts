import {withAsyncStatus, withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {ALERTS_LOAD_ERROR_MESSAGE} from '../../constants/alert/alert.constants';
import {alertsComputed} from './alerts.computed';
import {alertsEffects, alertsHooks} from './alerts.effects';
import {alertsMethods} from './alerts.methods';
import {initialAlertsState} from './alerts.state';

export const AlertsStore = signalStore(
  {providedIn: 'root'},
  withState(initialAlertsState),
  withAsyncStatus({defaultErrorMessage: ALERTS_LOAD_ERROR_MESSAGE}),
  withNamedAsyncStatus('append', {defaultErrorMessage: "Couldn't load more alerts."}),
  withMethods(alertsMethods),
  withComputed(alertsComputed),
  withMethods(alertsEffects),
  withHooks({onInit: alertsHooks})
);
