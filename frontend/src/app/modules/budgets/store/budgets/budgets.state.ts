import {type Nullable} from '@lifekit-hq/ui';

import {type BudgetSummaryResponse} from '../../models/budget/budget.model';

export interface BudgetsState {
  summary: Nullable<BudgetSummaryResponse>;
  /** The category whose estimate is being edited inline. */
  editingCategory: Nullable<string>;
  refreshFailed: boolean;
  selectedYear: number;
  selectedMonth: number;
}

const now = new Date();

export const initialBudgetsState: BudgetsState = {
  summary: null,
  editingCategory: null,
  refreshFailed: false,
  selectedYear: now.getFullYear(),
  selectedMonth: now.getMonth() + 1,
};
