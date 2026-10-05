import {signalState} from '@ngrx/signals';
import {describe, expect, it} from 'vitest';

import {
  type Subscription,
  type SubscriptionSummary,
} from '../../models/subscription/subscription.model';
import {subscriptionsMethods} from './subscriptions.methods';
import {initialSubscriptionsState} from './subscriptions.state';

function mkSubscription(overrides: Partial<Subscription> = {}): Subscription {
  return {
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
    isTracked: true,
    ...overrides,
  };
}

function mkSummary(overrides: Partial<SubscriptionSummary> = {}): SubscriptionSummary {
  return {
    subscriptions: {
      monthly: 25,
      next12Months: 300,
      remainingCommitment: null,
      activeCount: 2,
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
      monthly: 25,
      next12Months: 300,
      remainingCommitment: null,
      activeCount: 2,
      hasUnknownSchedule: false,
    },
    potentiallyCancelledCount: 0,
    currency: 'EUR',
    ...overrides,
  };
}

describe('subscriptionsMethods', () => {
  it('setData stores subscriptions and resets status', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);

    methods.setData([mkSubscription()], true);

    expect(state.subscriptions()).toHaveLength(1);
    expect(state.hasInsufficientHistory()).toBe(true);
    expect(state.status()).toBe('idle');
  });

  it('setAddError stores and clears the add error code', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);

    methods.setAddError('COMMITMENT_ALREADY_TRACKED');
    expect(state.addErrorCode()).toBe('COMMITMENT_ALREADY_TRACKED');

    methods.setAddError(null);
    expect(state.addErrorCode()).toBeNull();
  });

  it('setAddError keeps the server text and clears it with the code', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);

    methods.setAddError('COMMITMENT_ALREADY_TRACKED', 'These charges are already tracked as Acme.');
    expect(state.addErrorDetail()).toBe('These charges are already tracked as Acme.');

    methods.setAddError(null);
    expect(state.addErrorDetail()).toBeNull();
  });

  it('setSummary stores the summary', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);

    methods.setSummary(mkSummary());

    expect(state.summary()).toEqual(mkSummary());
  });

  it('dismissSubscription flips only the matching row to dismissed', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);
    methods.setData([mkSubscription({id: 'a'}), mkSubscription({id: 'b'})], false);

    methods.dismissSubscription('a');

    expect(state.subscriptions().find(s => s.id === 'a')?.status).toBe('dismissed');
    expect(state.subscriptions().find(s => s.id === 'b')?.status).toBe('active');
  });

  it('dismissSubscription keeps the summary intact', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);
    methods.setData([mkSubscription({id: 'a'})], false);
    methods.setSummary(mkSummary());

    methods.dismissSubscription('a');

    expect(state.summary()).toEqual(mkSummary());
  });

  it('restoreSubscription flips only the matching row back to active', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);
    methods.setData(
      [
        mkSubscription({id: 'a', status: 'dismissed'}),
        mkSubscription({id: 'b', status: 'dismissed'}),
      ],
      false
    );

    methods.restoreSubscription('a');

    expect(state.subscriptions().find(s => s.id === 'a')?.status).toBe('active');
    expect(state.subscriptions().find(s => s.id === 'b')?.status).toBe('dismissed');
  });

  it('restoreSubscription keeps the summary intact', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);
    methods.setData([mkSubscription({id: 'a', status: 'dismissed'})], false);
    methods.setSummary(mkSummary());

    methods.restoreSubscription('a');

    expect(state.summary()).toEqual(mkSummary());
  });

  it('setSort stores the sort', () => {
    const state = signalState(initialSubscriptionsState);
    const methods = subscriptionsMethods(state);

    methods.setSort('amount');

    expect(state.sort()).toBe('amount');
  });
});
