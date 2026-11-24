import {computed, type Signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';

import {type BookIncome} from '../../models/book-income/book-income.model';

const PERCENT = 100;

interface StoreState {
  income: Signal<Nullable<BookIncome>>;
}

export function bookIncomeComputed(store: StoreState) {
  return {
    /** The tile has something to say once income was received or a holding has a forward estimate. */
    hasIncome: computed(() => {
      const income = store.income();
      return income !== null && (income.ttm.gross > 0 || income.holdings.length > 0);
    }),
    holdings: computed(() => store.income()?.holdings ?? []),
    /** The yield on cost as a one-decimal percent, e.g. "5.7%". */
    yieldOnCostLabel: computed(() => {
      const value = store.income()?.yieldOnCost;
      return value === null || value === undefined ? null : `${(value * PERCENT).toFixed(1)}%`;
    }),
  };
}
