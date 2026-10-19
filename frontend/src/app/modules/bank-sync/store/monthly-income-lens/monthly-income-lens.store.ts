import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {monthlyIncomeLensComputed} from './monthly-income-lens.computed';
import {monthlyIncomeLensEffects, monthlyIncomeLensHooks} from './monthly-income-lens.effects';
import {monthlyIncomeLensMethods} from './monthly-income-lens.methods';
import {initialMonthlyIncomeLensState} from './monthly-income-lens.state';

export const MonthlyIncomeLensStore = signalStore(
  withState(initialMonthlyIncomeLensState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load the monthly income figure.'}),
  withMethods(monthlyIncomeLensMethods),
  withComputed(monthlyIncomeLensComputed),
  withMethods(monthlyIncomeLensEffects),
  withHooks({onInit: monthlyIncomeLensHooks})
);
