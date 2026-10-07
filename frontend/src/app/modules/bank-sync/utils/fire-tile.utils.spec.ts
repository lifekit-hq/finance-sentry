import {describe, expect, it} from 'vitest';

import {type FireProjection} from '../models/fire/fire.model';
import {FireTileUtils} from './fire-tile.utils';

function fire(overrides: Partial<FireProjection> = {}): FireProjection {
  return {
    status: 'Projected',
    target: 1_000_000,
    currentNetWorth: 250_000,
    monthlySavings: 2_000,
    annualSpend: 40_000,
    safeWithdrawalRate: 0.04,
    realAnnualReturn: 0.05,
    projectedDate: '2041-03-26',
    monthsToFire: 150.2,
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    ...overrides,
  };
}

describe('FireTileUtils.formatDate', () => {
  it('renders month and year', () => {
    expect(FireTileUtils.formatDate('2041-03-26')).toBe('Mar 2041');
  });

  it('does not slip a month across the UTC boundary', () => {
    expect(FireTileUtils.formatDate('2041-04-01')).toBe('Apr 2041');
  });
});

describe('FireTileUtils.formatDuration', () => {
  it('names years and months, rounding the fraction up', () => {
    expect(FireTileUtils.formatDuration(75.2)).toBe('6 years 4 months');
  });

  it('uses the singular', () => {
    expect(FireTileUtils.formatDuration(13)).toBe('1 year 1 month');
  });

  it('omits a zero part', () => {
    expect(FireTileUtils.formatDuration(24)).toBe('2 years');
    expect(FireTileUtils.formatDuration(8)).toBe('8 months');
  });

  it('says less than a month for zero', () => {
    expect(FireTileUtils.formatDuration(0)).toBe('less than a month');
  });
});

describe('FireTileUtils.progressPercent', () => {
  it('rounds the share of the target', () => {
    expect(FireTileUtils.progressPercent(250_000, 1_000_000)).toBe(25);
  });

  it('clamps above the target to 100', () => {
    expect(FireTileUtils.progressPercent(2_000_000, 1_000_000)).toBe(100);
  });

  it('clamps negative net worth to 0', () => {
    expect(FireTileUtils.progressPercent(-5_000, 1_000_000)).toBe(0);
  });

  it('is 0 when there is no target', () => {
    expect(FireTileUtils.progressPercent(10_000, 0)).toBe(0);
  });
});

describe('FireTileUtils.headline', () => {
  it('states the date when projected', () => {
    expect(FireTileUtils.headline(fire())).toBe('Financial independence around Mar 2041');
  });

  it('states that the target is reached', () => {
    expect(FireTileUtils.headline(fire({status: 'AlreadyReached', projectedDate: null}))).toBe(
      'You have reached your financial independence target'
    );
  });

  it('explains the absence of a date when nothing is being saved', () => {
    expect(FireTileUtils.headline(fire({status: 'NotSaving', projectedDate: null}))).toBe(
      'No date yet — nothing is being saved at the current rate'
    );
  });

  it('is empty without enough history', () => {
    expect(FireTileUtils.headline(fire({status: 'InsufficientHistory'}))).toBe('');
  });
});

describe('FireTileUtils.runway', () => {
  it('gives the time to go while a date exists', () => {
    expect(FireTileUtils.runway(fire({monthsToFire: 150.2}))).toBe(
      'About 12 years 7 months from now'
    );
  });

  it('is absent for any other state', () => {
    expect(FireTileUtils.runway(fire({status: 'NotSaving', monthsToFire: null}))).toBeNull();
  });
});

describe('FireTileUtils.assumptions', () => {
  it('spells out the target, both assumptions and the inputs in words', () => {
    const text = FireTileUtils.assumptions(fire());
    expect(text).toContain('Target $1,000,000.00');
    expect(text).toContain('$40,000.00');
    expect(text).toContain('4% safe withdrawal rate');
    expect(text).toContain('5% real (after-inflation) annual return');
    expect(text).toContain('$250,000.00 net worth');
    expect(text).toContain('$2,000.00 a month');
    expect(text).toContain('compounded monthly');
  });

  it('formats every amount in the base currency, never as dollars', () => {
    const text = FireTileUtils.assumptions(fire({baseCurrency: 'EUR'}));
    expect(text).toContain('Target €1,000,000.00');
    expect(text).toContain('€40,000.00');
    expect(text).toContain('€250,000.00 net worth');
    expect(text).toContain('€2,000.00 a month');
    expect(text).not.toContain('$');
  });

  it('shows a fractional rate as typed', () => {
    expect(FireTileUtils.assumptions(fire({safeWithdrawalRate: 0.035}))).toContain(
      '3.5% safe withdrawal rate'
    );
  });
});
