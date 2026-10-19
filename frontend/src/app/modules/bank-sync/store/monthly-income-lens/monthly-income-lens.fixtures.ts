import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';

export function monthlyIncomeLens(overrides: Partial<MonthlyIncomeLens> = {}): MonthlyIncomeLens {
  return {
    status: 'Shown',
    netWorth: 300_000,
    safeWithdrawalRate: 0.04,
    monthlyIncome: 1_000,
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    ...overrides,
  };
}
