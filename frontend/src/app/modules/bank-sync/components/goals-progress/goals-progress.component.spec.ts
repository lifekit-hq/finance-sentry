import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type GoalsProgress} from '../../models/goals-progress/goals-progress.model';
import {goalsProgressComputed} from '../../store/goals-progress/goals-progress.computed';
import {GoalsProgressStore} from '../../store/goals-progress/goals-progress.store';
import {GoalsProgressComponent} from './goals-progress.component';

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
      {
        label: 'Someday',
        targetAmount: null,
        targetDate: null,
        priority: 'low',
        monthsLeft: null,
        requiredMonthlySaving: null,
        projectedAtTargetDate: null,
        alreadyReached: false,
        onTrack: 'NoTargetAmount',
        reason: null,
      },
    ],
    contributionPlan: null,
    realAnnualReturnRange: {low: 0.02, central: 0.05, high: 0.08},
    hasStaleSleeves: false,
    baseCurrency: 'USD',
    basis: 'basis',
    disclosure: 'Hypothetical, not a guarantee.',
    ...overrides,
  };
}

function render(
  value: Nullable<GoalsProgress>,
  state: {isLoading?: boolean; errorMessage?: string} = {}
) {
  const load = vi.fn();
  const progressSignal = signal(value);
  TestBed.configureTestingModule({
    providers: [{provide: ConnectivityService, useValue: {offline: () => false}}],
  });
  // The factory runs in the component's injection context, where the computeds read ConnectivityService.
  TestBed.overrideComponent(GoalsProgressComponent, {
    set: {
      providers: [
        {
          provide: GoalsProgressStore,
          useFactory: () => ({
            progress: progressSignal,
            load,
            ...goalsProgressComputed({
              progress: progressSignal,
              isLoading: signal(state.isLoading ?? false),
              errorMessage: signal(state.errorMessage ?? ''),
            }),
          }),
        },
      ],
    },
  });
  const fixture = TestBed.createComponent(GoalsProgressComponent);
  fixture.detectChanges();
  return {el: fixture.nativeElement as HTMLElement, load};
}

function text(el: Element, testId: string): string {
  return (
    el.querySelector(`[data-testid="${testId}"]`)?.textContent?.replace(/\s+/g, ' ').trim() ?? ''
  );
}

describe('GoalsProgressComponent', () => {
  it('shows each goal with its target, verdict and required saving', () => {
    const {el} = render(progress());
    const [house] = Array.from(el.querySelectorAll('[data-testid="goal-row"]'));
    expect(house?.textContent).toContain('House deposit');
    expect(text(house, 'goal-track')).toBe('On track');
    expect(text(house, 'goal-target')).toBe(
      '$130,000 by Sept 2028 · 24 months left · high priority'
    );
    expect(text(house, 'goal-required')).toContain('Needs about $785 a month');
    expect(text(house, 'goal-projected')).toContain('$135,000');
  });

  it('keeps a goal it cannot track on screen, and says what is missing', () => {
    const {el} = render(progress());
    const rows = Array.from(el.querySelectorAll('[data-testid="goal-row"]'));
    expect(rows).toHaveLength(2);
    expect(text(rows[1], 'goal-track')).toBe('No target amount');
    expect(text(rows[1], 'goal-note')).toContain('Add a target amount');
    expect(rows[1]?.querySelector('[data-testid="goal-required"]')).toBeNull();
  });

  it('says what the goals are measured against, with the disclosure', () => {
    const {el} = render(progress());
    const assumptions = text(el, 'goals-assumptions');
    expect(assumptions).toContain('whole net worth plus your savings');
    expect(assumptions).toContain('Hypothetical, not a guarantee.');
  });

  it('shows no plan line when the IPS has no plan', () => {
    expect(render(progress()).el.querySelector('[data-testid="goals-plan"]')).toBeNull();
  });

  it('shows the plan line when the IPS has a plan', () => {
    const {el} = render(
      progress({
        contributionPlan: {
          plannedAmount: 500,
          cadence: 'monthly',
          plannedMonthly: 500,
          actualMonthlySavings: 1_000,
          difference: 500,
          reason: null,
        },
      })
    );
    expect(text(el, 'goals-plan')).toContain('Your IPS plans $500 monthly');
  });

  it('states amounts in the base currency', () => {
    const {el} = render(progress({baseCurrency: 'EUR'}));
    expect(text(el, 'goal-target')).toContain('€130,000');
  });

  it('carries the staleness notice when sleeves are stale', () => {
    const {el} = render(progress({hasStaleSleeves: true}));
    expect(text(el, 'goals-stale')).toContain('stale');
  });

  it('shows the empty state, which says how to add a goal', () => {
    const {el} = render(progress({status: 'NoGoals', goals: []}));
    expect(el.textContent).toContain('No goals yet');
    expect(el.textContent).toContain('save_ips');
    expect(el.querySelector('[data-testid="goals-list"]')).toBeNull();
  });

  it('shows neither goals nor a verdict while the first load runs', () => {
    const {el} = render(null, {isLoading: true});
    expect(el.querySelector('[data-testid="goals-list"]')).toBeNull();
    expect(el.textContent).not.toContain('No goals yet');
    expect(el.textContent).not.toContain('Failed');
  });

  it('shows the error with a retry that reloads', () => {
    const {el, load} = render(null, {errorMessage: 'Failed to load goal progress.'});
    expect(el.textContent).toContain('Failed to load goal progress.');
    const retry = Array.from(el.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Retry')
    );
    retry?.click();
    expect(load).toHaveBeenCalled();
  });
});
