import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it, vi} from 'vitest';

import {ConnectivityService} from '../../../../shared/services/connectivity.service';
import {type CashflowProfile} from '../../models/cashflow-profile/cashflow-profile.model';
import {cashflowProfileComputed} from '../../store/cashflow-profile/cashflow-profile.computed';
import {CashflowProfileStore} from '../../store/cashflow-profile/cashflow-profile.store';
import {CashflowProfileComponent} from './cashflow-profile.component';

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

function render(
  value: Nullable<CashflowProfile>,
  state: {isLoading?: boolean; errorMessage?: string} = {}
) {
  const load = vi.fn();
  const profileSignal = signal(value);
  TestBed.configureTestingModule({
    providers: [{provide: ConnectivityService, useValue: {offline: () => false}}],
  });
  // The factory runs in the component's injection context, where the computeds read ConnectivityService.
  TestBed.overrideComponent(CashflowProfileComponent, {
    set: {
      providers: [
        {
          provide: CashflowProfileStore,
          useFactory: () => ({
            profile: profileSignal,
            load,
            ...cashflowProfileComputed({
              profile: profileSignal,
              isLoading: signal(state.isLoading ?? false),
              errorMessage: signal(state.errorMessage ?? ''),
            }),
          }),
        },
      ],
    },
  });
  const fixture = TestBed.createComponent(CashflowProfileComponent);
  fixture.detectChanges();
  return {el: fixture.nativeElement as HTMLElement, load};
}

function text(el: HTMLElement, testId: string): string {
  return el.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? '';
}

describe('CashflowProfileComponent', () => {
  it('shows burn, savings rate and runway with the arithmetic in words', () => {
    const {el} = render(profile());
    expect(text(el, 'cashflow-burn')).toBe('$3,000');
    expect(text(el, 'cashflow-rate')).toBe('25%');
    expect(text(el, 'cashflow-runway')).toBe('6 months');
    expect(text(el, 'cashflow-assumptions')).toContain('median over the 6 complete months');
  });

  it('states the burn in the base currency', () => {
    const {el} = render(profile({baseCurrency: 'EUR', burnMonthlyMedian: 2_800}));
    expect(text(el, 'cashflow-burn')).toBe('€2,800');
  });

  it('shows a dash and the reason when there is no runway', () => {
    const {el} = render(profile({runwayMonths: null, runwayReason: 'NO_CASH_SPLIT'}));
    expect(text(el, 'cashflow-runway')).toBe('—');
    expect(text(el, 'cashflow-runway-note')).toContain("doesn't split out cash");
  });

  it('keeps burn and rate on screen when there is no cash', () => {
    const {el} = render(profile({status: 'NoCash', runwayMonths: null, runwayReason: 'NO_CASH'}));
    expect(text(el, 'cashflow-burn')).toBe('$3,000');
    expect(text(el, 'cashflow-rate')).toBe('25%');
    expect(text(el, 'cashflow-runway')).toBe('—');
  });

  it('shows a negative rate as dissaving', () => {
    const {el} = render(profile({savingsRate: -0.2}));
    expect(text(el, 'cashflow-rate')).toBe('-20%');
    expect(el.textContent).toContain('spending more than came in');
  });

  it('carries the staleness notice when sleeves are stale', () => {
    const {el} = render(profile({hasStaleSleeves: true}));
    expect(text(el, 'cashflow-stale')).toContain('stale');
  });

  it('shows the empty state, not figures, below the complete-month minimum', () => {
    const {el} = render(
      profile({
        status: 'InsufficientHistory',
        completeMonths: 2,
        savingsRate: null,
        burnMonthlyMedian: null,
        incomeMonthlyMedian: null,
        runwayMonths: null,
        reason: 'INSUFFICIENT_MONTHS',
      })
    );
    expect(el.textContent).toContain('Not enough history yet');
    expect(el.querySelector('[data-testid="cashflow-figures"]')).toBeNull();
  });

  it('shows neither figures nor a verdict while the first load runs', () => {
    const {el} = render(null, {isLoading: true});
    expect(el.querySelector('[data-testid="cashflow-figures"]')).toBeNull();
    // The primitive draws its skeleton only after its show delay, so the region reads as neither empty nor failed.
    expect(el.textContent).not.toContain('Not enough history yet');
    expect(el.textContent).not.toContain('Failed');
  });

  it('shows the error with a retry that reloads', () => {
    const {el, load} = render(null, {
      errorMessage: 'Failed to load burn, savings rate and runway.',
    });
    expect(el.textContent).toContain('Failed to load burn, savings rate and runway.');
    const retry = Array.from(el.querySelectorAll('button')).find(b =>
      b.textContent?.includes('Retry')
    );
    retry?.click();
    expect(load).toHaveBeenCalled();
  });
});
