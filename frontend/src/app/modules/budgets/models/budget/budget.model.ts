export interface Budget {
  id: string;
  category: string;
  categoryLabel: string;
  monthlyLimit: number;
  currency: string;
  createdAt: string;
}

export interface BudgetSummaryItem {
  id: string;
  category: string;
  categoryLabel: string;
  monthlyLimit: number;
  spent: number;
  remaining: number;
  isOverBudget: boolean;
  currency: string;
  paceRatio: number;
  projectedMonthEndSpend: number;
  isOffPace: boolean;
}

export interface CreateBudgetRequest {
  category: string;
  monthlyLimit: number;
}

export interface UpdateBudgetRequest {
  monthlyLimit: number;
}

export interface BudgetsListResponse {
  items: Budget[];
  totalCount: number;
}

export interface BudgetSummaryResponse {
  year: number;
  month: number;
  items: BudgetSummaryItem[];
  totalLimit: number;
  totalSpent: number;
}
