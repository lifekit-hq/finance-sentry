import {patchState, type WritableStateSource} from '@ngrx/signals';

import {type BookIncome} from '../../models/book-income/book-income.model';
import {type BookIncomeState} from './book-income.state';

export function bookIncomeMethods(store: WritableStateSource<BookIncomeState>) {
  return {
    setIncome(income: BookIncome): void {
      patchState(store, {income});
    },
  };
}
