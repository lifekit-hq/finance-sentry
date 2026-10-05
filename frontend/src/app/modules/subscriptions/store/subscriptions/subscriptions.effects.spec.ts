import {HttpErrorResponse} from '@angular/common/http';
import {TestBed} from '@angular/core/testing';
import {of, throwError} from 'rxjs';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type Subscription,
  type SubscriptionsListResponse,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';
import {SubscriptionsService} from '../../services/subscriptions.service';
import {subscriptionsEffects} from './subscriptions.effects';

const SUBSCRIPTION: Subscription = {
  id: 'sub-1',
  merchantName: 'Netflix',
  cadence: 'monthly',
  averageAmount: 10,
  lastKnownAmount: 10,
  monthlyEquivalent: 10,
  currency: 'EUR',
  lastChargeDate: '2026-08-01',
  nextExpectedDate: '2026-09-01',
  status: 'active',
  occurrenceCount: 3,
  kind: 'subscription',
  termCount: null,
  endDate: null,
  startDate: null,
  remainingPayments: null,
  isManual: false,
};

const LIST_RESPONSE: SubscriptionsListResponse = {
  items: [SUBSCRIPTION],
  totalCount: 1,
  hasInsufficientHistory: false,
};

const SUMMARY: SubscriptionSummary = {
  subscriptions: {
    monthly: 10,
    next12Months: 120,
    remainingCommitment: null,
    activeCount: 1,
    hasUnknownSchedule: false,
  },
  installments: {
    monthly: 0,
    next12Months: 0,
    remainingCommitment: 0,
    activeCount: 0,
    hasUnknownSchedule: false,
  },
  combined: {
    monthly: 10,
    next12Months: 120,
    remainingCommitment: null,
    activeCount: 1,
    hasUnknownSchedule: false,
  },
  potentiallyCancelledCount: 0,
  currency: 'EUR',
};

function buildStore() {
  return {
    setData: vi.fn(),
    setSummary: vi.fn(),
    dismissSubscription: vi.fn(),
    restoreSubscription: vi.fn(),
    setAddError: vi.fn(),
  };
}

function buildService() {
  return {
    getSubscriptions: vi.fn(),
    getSummary: vi.fn(),
    dismiss: vi.fn(),
    restore: vi.fn(),
    add: vi.fn(),
  };
}

function configure(service: ReturnType<typeof buildService>) {
  TestBed.configureTestingModule({
    providers: [{provide: SubscriptionsService, useValue: service}],
  });
}

describe('subscriptionsEffects', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('load: stores list and summary', () => {
    const store = buildStore();
    const service = buildService();
    service.getSubscriptions.mockReturnValue(of(LIST_RESPONSE));
    service.getSummary.mockReturnValue(of(SUMMARY));
    configure(service);

    TestBed.runInInjectionContext(() => subscriptionsEffects(store).load());

    expect(service.getSubscriptions).toHaveBeenCalledWith(true);
    expect(store.setData).toHaveBeenCalledWith([SUBSCRIPTION], false);
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('dismiss: updates the list row and refetches the summary', () => {
    const store = buildStore();
    const service = buildService();
    service.dismiss.mockReturnValue(of(void 0));
    service.getSummary.mockReturnValue(of(SUMMARY));
    configure(service);

    TestBed.runInInjectionContext(() => subscriptionsEffects(store).dismiss('sub-1'));

    expect(service.dismiss).toHaveBeenCalledWith('sub-1');
    expect(store.dismissSubscription).toHaveBeenCalledWith('sub-1');
    expect(service.getSummary).toHaveBeenCalled();
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('restore: updates the list row and refetches the summary', () => {
    const store = buildStore();
    const service = buildService();
    service.restore.mockReturnValue(of(void 0));
    service.getSummary.mockReturnValue(of(SUMMARY));
    configure(service);

    TestBed.runInInjectionContext(() => subscriptionsEffects(store).restore('sub-1'));

    expect(service.restore).toHaveBeenCalledWith('sub-1');
    expect(store.restoreSubscription).toHaveBeenCalledWith('sub-1');
    expect(service.getSummary).toHaveBeenCalled();
    expect(store.setSummary).toHaveBeenCalledWith(SUMMARY);
  });

  it('addCommitment: posts the pick and reloads the list', () => {
    const store = buildStore();
    const service = buildService();
    service.add.mockReturnValue(of({id: 'new'}));
    service.getSubscriptions.mockReturnValue(of(LIST_RESPONSE));
    service.getSummary.mockReturnValue(of(SUMMARY));
    configure(service);
    const payload = {
      transactionId: 'tx-1',
      kind: 'subscription' as const,
      merchant: 'Acme',
      monthlyAmount: 10,
      termCount: null,
    };

    TestBed.runInInjectionContext(() => subscriptionsEffects(store).addCommitment(payload));

    expect(service.add).toHaveBeenCalledWith(payload);
    expect(store.setAddError).toHaveBeenCalledWith(null);
    expect(store.setData).toHaveBeenCalledWith([SUBSCRIPTION], false);
  });

  it('addCommitment: keeps the error code when the add is refused', () => {
    const store = buildStore();
    const service = buildService();
    service.add.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({status: 409, error: {errorCode: 'COMMITMENT_ALREADY_TRACKED'}})
      )
    );
    configure(service);

    TestBed.runInInjectionContext(() =>
      subscriptionsEffects(store).addCommitment({
        transactionId: 'tx-1',
        kind: 'installment',
        merchant: 'Acme',
        monthlyAmount: 10,
        termCount: 12,
      })
    );

    expect(store.setAddError).toHaveBeenLastCalledWith('COMMITMENT_ALREADY_TRACKED');
    expect(store.setData).not.toHaveBeenCalled();
  });
});
