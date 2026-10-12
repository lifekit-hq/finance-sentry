import {withAsyncStatus, withUrlSync} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {BUDGETS_LOAD_ERROR_MESSAGE} from '../../constants/budget/budget.constants';
import {budgetsComputed} from './budgets.computed';
import {budgetsEffects, budgetsHooks} from './budgets.effects';
import {budgetsMethods} from './budgets.methods';
import {initialBudgetsState} from './budgets.state';

export const BudgetsStore = signalStore(
  withState(initialBudgetsState),
  withUrlSync({
    selectedYear: {param: 'year', default: initialBudgetsState.selectedYear, codec: 'number'},
    selectedMonth: {param: 'month', default: initialBudgetsState.selectedMonth, codec: 'number'},
  }),
  withAsyncStatus({defaultErrorMessage: BUDGETS_LOAD_ERROR_MESSAGE}),
  withMethods(budgetsMethods),
  withComputed(budgetsComputed),
  withMethods(budgetsEffects),
  withHooks({onInit: budgetsHooks})
);
