import {withNamedAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {incomeSourcesComputed} from './income-sources.computed';
import {incomeSourcesEffects, incomeSourcesHooks} from './income-sources.effects';
import {incomeSourcesMethods} from './income-sources.methods';
import {initialIncomeSourcesState} from './income-sources.state';

export const IncomeSourcesStore = signalStore(
  withState(initialIncomeSourcesState),
  withNamedAsyncStatus('sources', {defaultErrorMessage: 'Failed to load income sources.'}),
  withNamedAsyncStatus('save', {defaultErrorMessage: 'Could not save the income source.'}),
  withMethods(incomeSourcesMethods),
  withComputed(incomeSourcesComputed),
  withMethods(incomeSourcesEffects),
  withHooks({onInit: incomeSourcesHooks})
);
