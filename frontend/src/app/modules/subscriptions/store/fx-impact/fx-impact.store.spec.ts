import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {NEVER, of, throwError} from 'rxjs';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {FX_IMPACT_ENABLED_STORAGE_KEY} from '../../constants/fx-impact/fx-impact.constants';
import {type FxImpact} from '../../models/fx-impact/fx-impact.model';
import {type Subscription} from '../../models/subscription/subscription.model';
import {SubscriptionsService} from '../../services/subscriptions.service';
import {SubscriptionsStore} from '../subscriptions/subscriptions.store';
import {FxImpactStore} from './fx-impact.store';

const IMPACT: FxImpact = {
  baseCurrency: 'EUR',
  plans: [
    {
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
    },
  ],
  totals: {
    paidSaving: 321.4,
    remainingSaving: 803.2,
    totalSaving: 1124.6,
    hasUnknownRemainder: false,
  },
};

const SUBSCRIPTION: Subscription = {
  id: 's1',
  merchantName: 'Mortgage',
  cadence: 'monthly',
  averageAmount: 14060.96,
  lastKnownAmount: 14060.96,
  monthlyEquivalent: 14060.96,
  currency: 'UAH',
  lastChargeDate: '2026-08-11',
  nextExpectedDate: '2026-09-11',
  status: 'active',
  occurrenceCount: 28,
  kind: 'installment',
  termCount: 48,
  endDate: null,
  startDate: '2024-05-01',
  remainingPayments: 20,
  isManual: true,
  isTracked: true,
  latestPriceChange: null,
  category: null,
  iconId: null,
};

const EMPTY: FxImpact = {
  baseCurrency: 'EUR',
  plans: [],
  totals: {paidSaving: 0, remainingSaving: 0, totalSaving: 0, hasUnknownRemainder: false},
};

describe('FxImpactStore', () => {
  let service: {getFxImpact: ReturnType<typeof vi.fn>};
  let subscriptions: ReturnType<typeof signal<Subscription[]>>;

  function inject(): InstanceType<typeof FxImpactStore> {
    TestBed.configureTestingModule({
      providers: [
        FxImpactStore,
        {provide: SubscriptionsService, useValue: service},
        {provide: SubscriptionsStore, useValue: {subscriptions}},
      ],
    });
    const store = TestBed.inject(FxImpactStore);
    // The list signal is read through an effect, which runs on the first tick.
    TestBed.tick();
    return store;
  }

  beforeEach(() => {
    service = {getFxImpact: vi.fn().mockReturnValue(of(IMPACT))};
    subscriptions = signal([SUBSCRIPTION]);
  });

  afterEach(() => localStorage.clear());

  it('opens the gate when a foreign-currency plan comes back', () => {
    const store = inject();

    expect(store.available()).toBe(true);
    expect(store.showInsight()).toBe(true);
    expect(store.planViews()).toHaveLength(1);
    expect(store.summary()).toBe(
      'Rate moves have saved you €321.40 so far and will save €803.20 on the payments left.'
    );
  });

  it('keeps the gate closed when the server finds no such plan', () => {
    service.getFxImpact.mockReturnValue(of(EMPTY));
    const store = inject();

    expect(store.available()).toBe(false);
    expect(store.showInsight()).toBe(false);
  });

  it('stays closed until the page has a list to price', () => {
    subscriptions.set([]);
    const store = inject();

    expect(service.getFxImpact).not.toHaveBeenCalled();
    expect(store.available()).toBe(false);
  });

  it('stays closed while the read is in flight', () => {
    service.getFxImpact.mockReturnValue(NEVER);

    expect(inject().available()).toBe(false);
  });

  it('closes the gate quietly when the read fails', () => {
    service.getFxImpact.mockReturnValue(throwError(() => new Error('boom')));
    const store = inject();

    expect(store.available()).toBe(false);
  });

  it('closes the gate when a later read finds the plans gone', () => {
    const store = inject();
    service.getFxImpact.mockReturnValue(of(EMPTY));

    subscriptions.set([{...SUBSCRIPTION, id: 's2'}]);
    TestBed.tick();

    expect(store.available()).toBe(false);
  });

  it('reads again when the page list changes', () => {
    inject();

    subscriptions.set([SUBSCRIPTION, {...SUBSCRIPTION, id: 's2'}]);
    TestBed.tick();

    expect(service.getFxImpact).toHaveBeenCalledTimes(2);
  });

  it('hides the insight, but not its toggle, when switched off - and remembers it', () => {
    const store = inject();

    store.toggleEnabled(false);

    expect(store.available()).toBe(true);
    expect(store.showInsight()).toBe(false);
    expect(localStorage.getItem(FX_IMPACT_ENABLED_STORAGE_KEY)).toBe('0');
  });

  it("starts from this device's remembered off choice", () => {
    localStorage.setItem(FX_IMPACT_ENABLED_STORAGE_KEY, '0');
    const store = inject();

    expect(store.enabled()).toBe(false);
    expect(store.showInsight()).toBe(false);
  });

  it('flags plans with no known remainder', () => {
    service.getFxImpact.mockReturnValue(
      of({...IMPACT, totals: {...IMPACT.totals, hasUnknownRemainder: true}})
    );

    expect(inject().hasUnknownRemainder()).toBe(true);
  });
});
