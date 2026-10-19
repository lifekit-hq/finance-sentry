import {type NetWorthProjection} from '../../models/projection/projection.model';

export function netWorthProjection(
  overrides: Partial<NetWorthProjection> = {}
): NetWorthProjection {
  return {
    status: 'Projected',
    horizonMonths: 12,
    currentNetWorth: 10_000,
    monthlySavings: 1_000,
    completeMonths: 3,
    projectedNetWorth: {low: 22_312, central: 22_789, high: 23_270},
    realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    ...overrides,
  };
}
