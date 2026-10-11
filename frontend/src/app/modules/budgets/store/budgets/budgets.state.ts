import {type BudgetSummaryItem} from '../../models/budget/budget.model';

export interface BudgetsState {
  summaryItems: BudgetSummaryItem[];
  editingId: Nullable<string>;
  status: AsyncStatus;
  refreshFailed: boolean;
  selectedYear: number;
  selectedMonth: number;
}

const now = new Date();

export const initialBudgetsState: BudgetsState = {
  summaryItems: [],
  editingId: null,
  status: 'idle',
  refreshFailed: false,
  selectedYear: now.getFullYear(),
  selectedMonth: now.getMonth() + 1,
};
