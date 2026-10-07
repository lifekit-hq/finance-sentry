import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {type FireProjection} from '../../models/fire/fire.model';
import {fireComputed} from './fire.computed';

function build(projection: Nullable<FireProjection>, status: AsyncStatus = 'idle') {
  return fireComputed({projection: signal(projection), status: signal(status)});
}

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

describe('fireComputed', () => {
  it('is hidden before the projection loads', () => {
    expect(build(null).visible()).toBe(false);
  });

  it('is hidden below the complete-month minimum', () => {
    expect(build(fire({status: 'InsufficientHistory'})).visible()).toBe(false);
  });

  it.each(['Projected', 'AlreadyReached', 'NotSaving'] as const)('is shown for %s', status => {
    expect(build(fire({status})).visible()).toBe(true);
  });

  it('reports loading while the request is in flight', () => {
    expect(build(null, 'loading').isLoading()).toBe(true);
  });

  it('derives the progress share from net worth and target', () => {
    expect(build(fire()).progressPercent()).toBe(25);
  });

  it('flags stale sleeves so the tile can say the date is an estimate', () => {
    expect(build(fire({hasStaleSleeves: true})).hasStaleSleeves()).toBe(true);
    expect(build(fire()).hasStaleSleeves()).toBe(false);
  });
});
