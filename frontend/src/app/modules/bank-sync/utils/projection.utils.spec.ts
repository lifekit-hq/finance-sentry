import {describe, expect, it} from 'vitest';

import {type NetWorthProjection} from '../models/projection/projection.model';
import {PROJECTION_DISCLOSURE, ProjectionUtils} from './projection.utils';

function projection(overrides: Partial<NetWorthProjection> = {}): NetWorthProjection {
  return {
    status: 'Projected',
    horizonMonths: 12,
    currentNetWorth: 10_000,
    monthlySavings: 1_000,
    completeMonths: 3,
    projectedNetWorth: {low: 22_312.4, central: 22_788.9, high: 23_270.2},
    realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    ...overrides,
  };
}

describe('ProjectionUtils.returnRange', () => {
  it('names both bounds', () => {
    expect(ProjectionUtils.returnRange({low: 0.02, central: 0.05, high: 0.08})).toBe('2% to 8%');
  });

  it('collapses a range with no spread to one rate', () => {
    expect(ProjectionUtils.returnRange({low: 0.05, central: 0.05, high: 0.05})).toBe('5%');
  });
});

describe('ProjectionUtils.moneyRange', () => {
  it('names both bounds in whole units', () => {
    expect(ProjectionUtils.moneyRange({low: 1_000.4, central: 1_500, high: 2_000.6}, 'EUR')).toBe(
      '€1,000 to €2,001'
    );
  });

  it('collapses a range with no spread to one amount', () => {
    expect(ProjectionUtils.moneyRange({low: 1_000, central: 1_000, high: 1_000}, 'USD')).toBe(
      '$1,000'
    );
  });
});

describe('ProjectionUtils.netWorthCentral', () => {
  it('formats the central figure in whole units', () => {
    expect(ProjectionUtils.netWorthCentral(projection())).toBe('$22,789');
  });

  it('is an em dash without a projection', () => {
    expect(ProjectionUtils.netWorthCentral(projection({projectedNetWorth: null}))).toBe('—');
  });
});

describe('ProjectionUtils.netWorthRange', () => {
  it('formats the low-to-high range', () => {
    expect(ProjectionUtils.netWorthRange(projection())).toBe('$22,312 to $23,270');
  });

  it('is empty without a projection', () => {
    expect(ProjectionUtils.netWorthRange(projection({projectedNetWorth: null}))).toBe('');
  });
});

describe('ProjectionUtils.netWorthAssumptions', () => {
  it('states every input, the central return, the range and the disclosure', () => {
    const text = ProjectionUtils.netWorthAssumptions(projection({baseCurrency: 'EUR'}));
    expect(text).toContain('Your €10,000 net worth plus €1,000 a month saved');
    expect(text).toContain('your median over 3 complete months');
    expect(text).toContain('a 5% real (after-inflation) annual return');
    expect(text).toContain('the range uses 2% to 8%');
    expect(text).toContain("In today's money.");
    expect(text).toContain(PROJECTION_DISCLOSURE);
  });
});
