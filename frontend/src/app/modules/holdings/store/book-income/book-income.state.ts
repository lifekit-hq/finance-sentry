import {type Nullable} from '@lifekit-hq/ui';

import {type BookIncome} from '../../models/book-income/book-income.model';

export interface BookIncomeState {
  income: Nullable<BookIncome>;
}

export const initialBookIncomeState: BookIncomeState = {
  income: null,
};
