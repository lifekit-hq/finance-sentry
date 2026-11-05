import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';
import {cashflowProfileComputed} from './cashflow-profile.computed';

function profile(overrides: Partial<CashflowProfile> = {}): CashflowProfile {
  return {
    status: 'Shown',
    completeMonths: 6,
    fromMonth: '2026-03',
    toMonth: '2026-08',
    incomeMonthlyMedian: 4_000,
    burnMonthlyMedian: 3_000,
    savingsRate: 0.25,
    runwayMonths: 6,
    runwayReason: null,
    committedShare: 0.2,
    hasStaleSleeves: false,
    reason: null,
    baseCurrency: 'USD',
    disclosure: 'Context only.',
    ...overrides,
  };
}

function build(
  value: Nullable<CashflowProfile>,
  state: {isLoading?: boolean; errorMessage?: string; offline?: boolean} = {}
) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [{provide: ConnectivityService, useValue: {offline: () => state.offline ?? false}}],
  });
  return TestBed.runInInjectionContext(() =>
    cashflowProfileComputed({
      profile: signal(value),
      isLoading: signal(state.isLoading ?? false),
      errorMessage: signal(state.errorMessage ?? ''),
    })
  );
}

describe('cashflowProfileComputed', () => {
  it('shows the three figures as words', () => {
    const c = build(profile());
    expect(c.rate()).toBe('25%');
    expect(c.runway()).toBe('6 months');
    expect(c.rateNote()).toBe('of income kept');
    expect(c.runwayNote()).toBeNull();
    expect(c.figures()?.burnMonthlyMedian).toBe(3_000);
    expect(c.isEmpty()).toBe(false);
  });

  it('writes a negative rate as dissaving', () => {
    const c = build(profile({savingsRate: -0.2}));
    expect(c.rate()).toBe('-20%');
    expect(c.rateNote()).toBe('spending more than came in');
  });

  it('reads as empty, with no figures, while the profile is withheld', () => {
    const c = build(profile({status: 'InsufficientHistory', savingsRate: null}));
    expect(c.isEmpty()).toBe(true);
    expect(c.figures()).toBeNull();
    expect(c.rate()).toBeNull();
  });

  it('keeps burn and rate, and says why there is no runway, when there is no cash', () => {
    const c = build(profile({status: 'NoCash', runwayMonths: null, runwayReason: 'NO_CASH'}));
    expect(c.isEmpty()).toBe(false);
    expect(c.figures()).not.toBeNull();
    expect(c.runway()).toBeNull();
    expect(c.runwayNote()).toContain('No cash');
  });

  it('flags stale sleeves', () => {
    expect(build(profile({hasStaleSleeves: true})).hasStaleSleeves()).toBe(true);
    expect(build(profile()).hasStaleSleeves()).toBe(false);
  });

  it('shows a skeleton while the first load runs', () => {
    expect(build(null, {isLoading: true}).view().status).toBe('loading');
  });

  it('keeps the figures on a retry instead of a skeleton', () => {
    expect(build(profile(), {isLoading: true}).view().status).toBe('success');
  });

  it('reports a failed load as an error, never as empty', () => {
    const c = build(null, {errorMessage: 'Failed to load.'});
    expect(c.view().status).toBe('error');
    expect(c.isEmpty()).toBe(false);
  });

  it('explains what is missing before anything has loaded', () => {
    expect(build(null).emptySubMessage()).toContain('at least 3 complete months');
  });

  it('writes the assumptions only once there is a profile', () => {
    expect(build(null).assumptions()).toBe('');
    expect(build(profile()).assumptions()).toContain('median over the 6 complete months');
  });
});
