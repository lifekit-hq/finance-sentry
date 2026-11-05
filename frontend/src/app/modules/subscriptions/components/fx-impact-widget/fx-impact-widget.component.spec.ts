import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {describe, expect, it, vi} from 'vitest';

import {type FxImpactPlan} from '../../models/fx-impact/fx-impact.model';
import {FxImpactStore} from '../../store/fx-impact/fx-impact.store';
import {FxImpactUtils} from '../../utils/fx-impact.utils';
import {FxImpactWidgetComponent} from './fx-impact-widget.component';

const PLAN: FxImpactPlan = {
  id: 'p1',
  merchant: 'Mortgage',
  kind: 'installment',
  currency: 'UAH',
  paymentNative: 14060.96,
  signingDate: '2024-05-01',
  signingDateIsObserved: false,
  signingUnitsPerBase: 39.52,
  currentUnitsPerBase: 44.54,
  paymentCostAtSigning: 355.84,
  paymentCostNow: 315.68,
  costChangePercent: -11.29,
  paymentsMade: 28,
  paymentsRemaining: 20,
  remainingCostAtSigningRate: 7116.8,
  remainingCostAtTodayRate: 6313.6,
  remainingSaving: 803.2,
  paidSaving: 321.4,
  rates: [{date: '2024-05-01', unitsPerBase: 39.52}],
};

function render(
  options: {plans?: FxImpactPlan[]; enabled?: boolean; unknownRemainder?: boolean} = {}
) {
  const plans = options.plans ?? [PLAN];
  const enabled = signal(options.enabled ?? true);
  const available = signal(plans.length > 0);
  const store = {
    available,
    enabled,
    showInsight: signal(plans.length > 0 && enabled()),
    baseCurrency: signal('EUR'),
    summary: signal('Rate moves have saved you €321.40 so far.'),
    planViews: signal(plans.map(FxImpactUtils.planView)),
    hasUnknownRemainder: signal(options.unknownRemainder ?? false),
    toggleEnabled: vi.fn((value: boolean) => {
      enabled.set(value);
      store.showInsight.set(available() && value);
    }),
  };
  TestBed.overrideComponent(FxImpactWidgetComponent, {
    set: {providers: [{provide: FxImpactStore, useValue: store}]},
  });
  const fixture = TestBed.createComponent(FxImpactWidgetComponent);
  fixture.detectChanges();
  return {fixture, store, el: fixture.nativeElement as HTMLElement};
}

const text = (el: HTMLElement, id: string) =>
  el.querySelector(`[data-testid="${id}"]`)?.textContent?.replace(/\s+/g, ' ').trim();

describe('FxImpactWidgetComponent', () => {
  it('renders nothing, and no toggle, without a qualifying plan', () => {
    const {el} = render({plans: []});

    expect(el.hidden).toBe(true);
    expect(el.querySelector('[data-testid="fx-impact-widget"]')).toBeNull();
    expect(el.querySelector('[data-testid="fx-impact-toggle"]')).toBeNull();
  });

  it('shows the summary and each plan once such a plan exists', () => {
    const {el} = render();

    expect(el.hidden).toBe(false);
    expect(text(el, 'fx-impact-summary')).toBe('Rate moves have saved you €321.40 so far.');
    expect(el.querySelectorAll('[data-testid="fx-impact-plan"]')).toHaveLength(1);
    expect(text(el, 'fx-impact-rate')).toBe('39.52 → 44.54');
    expect(text(el, 'fx-impact-payment')).toContain('€355.84 → €315.68');
    expect(text(el, 'fx-impact-payment')).toContain('-11.3%');
    expect(text(el, 'fx-impact-remaining')).toContain('€6,313.60');
    expect(text(el, 'fx-impact-remaining')).toContain('vs €7,116.80 at signing');
    expect(text(el, 'fx-impact-remaining')).toContain('saves €803.20');
    expect(text(el, 'fx-impact-paid')).toBe('€321.40');
  });

  it('reads an unfavourable move as extra cost', () => {
    const {el} = render({plans: [{...PLAN, paidSaving: -50, remainingSaving: -120}]});

    expect(el.textContent).toContain('Extra cost so far');
    expect(text(el, 'fx-impact-paid')).toBe('€50.00');
    expect(text(el, 'fx-impact-remaining')).toContain('costs €120.00 more');
  });

  it('leaves out the payments-left line for a plan with no known remainder', () => {
    const {el} = render({
      plans: [
        {
          ...PLAN,
          paymentsRemaining: null,
          remainingCostAtSigningRate: null,
          remainingCostAtTodayRate: null,
          remainingSaving: null,
        },
      ],
      unknownRemainder: true,
    });

    expect(el.querySelector('[data-testid="fx-impact-remaining"]')).toBeNull();
    expect(el.textContent).toContain('Plans without a term set are left out');
  });

  it('flags a signing date that is only the first charge seen', () => {
    const {el} = render({plans: [{...PLAN, signingDateIsObserved: true}]});

    expect(el.textContent).toContain('(first charge seen)');
  });

  it('hides the insight but keeps the toggle when switched off', () => {
    const {el, fixture, store} = render();
    const toggle = el.querySelector('[data-testid="fx-impact-toggle"]') as HTMLElement;
    expect(toggle).not.toBeNull();

    store.toggleEnabled(false);
    fixture.detectChanges();

    expect(store.toggleEnabled).toHaveBeenCalledWith(false);
    expect(el.querySelector('[data-testid="fx-impact-summary"]')).toBeNull();
    expect(el.querySelector('[data-testid="fx-impact-toggle"]')).not.toBeNull();
  });
});
