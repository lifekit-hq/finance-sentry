import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {CASHFLOW_PROFILE_ERROR_MESSAGE} from '../../constants/cashflow-profile/cashflow-profile.constants';
import {cashflowProfileComputed} from './cashflow-profile.computed';
import {cashflowProfileEffects, cashflowProfileHooks} from './cashflow-profile.effects';
import {cashflowProfileMethods} from './cashflow-profile.methods';
import {initialCashflowProfileState} from './cashflow-profile.state';

export const CashflowProfileStore = signalStore(
  withState(initialCashflowProfileState),
  withAsyncStatus({defaultErrorMessage: CASHFLOW_PROFILE_ERROR_MESSAGE}),
  withMethods(cashflowProfileMethods),
  withComputed(cashflowProfileComputed),
  withMethods(cashflowProfileEffects),
  withHooks({onInit: cashflowProfileHooks})
);
