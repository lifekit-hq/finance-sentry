import {type BudgetSummaryItem} from '../../models/budget/budget.model';

export interface BudgetsState {
  summaryItems: BudgetSummaryItem[];
  editingId: Nullable<string>;
  refreshFailed: boolean;
  selectedYear: number;
  selectedMonth: number;
}

const now = new Date();

export const initialBudgetsState: BudgetsState = {
  summaryItems: [],
  editingId: null,
  refreshFailed: false,
  selectedYear: now.getFullYear(),
  selectedMonth: now.getMonth() + 1,
};
