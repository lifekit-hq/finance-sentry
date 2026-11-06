/** A hand-set amount sitting over one line's estimate. */
export interface Budget {
  id: string;
  category: string;
  categoryLabel: string;
  monthlyLimit: number;
  currency: string;
  createdAt: string;
}

/** Where a line's estimate comes from. `none` is spend with no estimate yet. */
export type EstimateSource = 'override' | 'history' | 'commitment' | 'none';

/** Why a month was left out of the history the estimates learn from. */
export type SkippedMonthReason = 'excluded' | 'coverage-gap';

export interface BudgetSummaryItem {
  /** The override row to edit or reset; null while the line runs on its suggestion. */
  overrideId: string | null;
  category: string;
  categoryLabel: string;
  estimate: number | null;
  /** What the line is estimated at without a hand edit; the amount "reset" returns to. */
  suggestedAmount: number | null;
  source: EstimateSource;
  spent: number;
  remaining: number;
  isOverBudget: boolean;
  currency: string;
  paceRatio: number;
  projectedMonthEndSpend: number;
  isOffPace: boolean;
  expectedMonthEnd: number;
}

export interface SkippedEstimateMonth {
  /** "yyyy-MM" */
  month: string;
  reason: SkippedMonthReason;
}

export interface CreateBudgetRequest {
  category: string;
  monthlyLimit: number;
}

export interface UpdateBudgetRequest {
  monthlyLimit: number;
}

export interface BudgetSummaryResponse {
  year: number;
  month: number;
  items: BudgetSummaryItem[];
  totalEstimate: number;
  totalSpent: number;
  expectedMonthEnd: number;
  hasEnoughHistory: boolean;
  historyMonthsUsed: number;
  minHistoryMonths: number;
  skippedMonths: SkippedEstimateMonth[];
  /** A coverage hole touches this month: its spend is only what reached the app (absent on older responses). */
  spendIncomplete?: boolean;
}

/** Saving a hand edit: creates the override, or updates the one already there. */
export interface SaveOverrideRequest {
  category: string;
  overrideId: string | null;
  amount: number;
}
