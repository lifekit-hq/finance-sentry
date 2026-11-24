import {withAsyncStatus} from '@lifekit-hq/core';
import {signalStore, withComputed, withHooks, withMethods, withState} from '@ngrx/signals';

import {bookIncomeComputed} from './book-income.computed';
import {bookIncomeEffects, bookIncomeHooks} from './book-income.effects';
import {bookIncomeMethods} from './book-income.methods';
import {initialBookIncomeState} from './book-income.state';

/** Component-scoped: the brokerage section's income-received tile. */
export const BookIncomeStore = signalStore(
  withState(initialBookIncomeState),
  withAsyncStatus({defaultErrorMessage: 'Failed to load income received.'}),
  withMethods(bookIncomeMethods),
  withComputed(bookIncomeComputed),
  withMethods(bookIncomeEffects),
  withHooks(bookIncomeHooks)
);
