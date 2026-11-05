import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';
import {goalsProgressComputed} from './goals-progress.computed';

function progress(overrides: Partial<GoalsProgress> = {}): GoalsProgress {
  return {
    status: 'Shown',
    currentNetWorth: 100_000,
    monthlySavings: 1_000,
    completeMonths: 6,
    goals: [
      {
        label: 'House deposit',
        targetAmount: 130_000,
        targetDate: '2028-09-26',
        priority: 'high',
        monthsLeft: 24,
        requiredMonthlySaving: {low: 700, central: 785, high: 900},
        projectedAtTargetDate: {low: 132_000, central: 135_000, high: 138_000},
        alreadyReached: false,
        onTrack: 'OnTrack',
        reason: null,
      },
    ],
    contributionPlan: {
      plannedAmount: 500,
      cadence: 'monthly',
      plannedMonthly: 500,
      actualMonthlySavings: 1_000,
      difference: 500,
      reason: null,
    },
    realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    basis: 'basis',
    disclosure: 'Hypothetical, not a guarantee.',
    ...overrides,
  };
}

function build(
  value: Nullable<GoalsProgress>,
  state: {isLoading?: boolean; errorMessage?: string; offline?: boolean} = {}
) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [{provide: ConnectivityService, useValue: {offline: () => state.offline ?? false}}],
  });
  return TestBed.runInInjectionContext(() =>
    goalsProgressComputed({
      progress: signal(value),
      isLoading: signal(state.isLoading ?? false),
      errorMessage: signal(state.errorMessage ?? ''),
    })
  );
}

describe('goalsProgressComputed', () => {
  it('draws a row per goal, the plan line and the assumptions', () => {
    const c = build(progress());
    expect(c.rows()).toHaveLength(1);
    expect(c.plan()).toContain('Your IPS plans $500 monthly');
    expect(c.assumptions()).toContain('whole net worth plus your savings');
    expect(c.isEmpty()).toBe(false);
  });

  it('reads as empty, with no rows, when the IPS has no goals', () => {
    const c = build(progress({status: 'NoGoals', goals: [], contributionPlan: null}));
    expect(c.isEmpty()).toBe(true);
    expect(c.rows()).toEqual([]);
    expect(c.plan()).toBeNull();
    expect(c.assumptions()).toBe('');
  });

  it('shows nothing before anything has loaded', () => {
    const c = build(null);
    expect(c.rows()).toEqual([]);
    expect(c.plan()).toBeNull();
    expect(c.assumptions()).toBe('');
    expect(c.isEmpty()).toBe(false);
  });

  it('flags stale sleeves', () => {
    expect(build(progress({hasStaleSleeves: true})).hasStaleSleeves()).toBe(true);
    expect(build(progress()).hasStaleSleeves()).toBe(false);
  });

  it('shows a skeleton while the first load runs', () => {
    expect(build(null, {isLoading: true}).view().status).toBe('loading');
  });

  it('keeps the goals on a retry instead of a skeleton', () => {
    expect(build(progress(), {isLoading: true}).view().status).toBe('success');
  });

  it('reports a failed load as an error, never as empty', () => {
    const c = build(null, {errorMessage: 'Failed to load.'});
    expect(c.view().status).toBe('error');
    expect(c.isEmpty()).toBe(false);
  });
});
