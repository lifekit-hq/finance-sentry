import {signal} from '@angular/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {
  type Subscription,
  type SubscriptionSort,
} from '../../models/subscription/subscription.model';
import {subscriptionsComputed} from './subscriptions.computed';

const NOW = new Date('2026-09-22T12:00:00Z');

function sub(id: string, nextExpectedDate: string, overrides: Partial<Subscription> = {}) {
  return {
    id,
    merchantName: id,
    cadence: 'monthly',
    averageAmount: 10,
    lastKnownAmount: 10,
    monthlyEquivalent: 10,
    currency: 'USD',
    lastChargeDate: '2026-08-22',
    nextExpectedDate,
    status: 'active',
    occurrenceCount: 3,
    kind: 'subscription',
    termCount: null,
    endDate: null,
    startDate: null,
    remainingPayments: null,
    isManual: false,
    ...overrides,
  } satisfies Subscription;
}

function build(subscriptions: Subscription[], sort: SubscriptionSort = 'date') {
  return subscriptionsComputed({
    subscriptions: signal(subscriptions),
    sort: signal(sort),
    summary: signal(null),
  });
}

describe('subscriptionsComputed activeSections', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('splits active subscriptions into due this week and the rest', () => {
    const sections = build([
      sub('soon', '2026-09-25'),
      sub('later', '2026-10-20'),
    ]).activeSections();

    expect(sections.map(s => [s.id, s.label, s.items.map(i => i.id)])).toEqual([
      ['due', 'Due this week', ['soon']],
      ['later', 'Later', ['later']],
    ]);
  });

  it('leaves out empty sections', () => {
    expect(
      build([sub('later', '2026-10-20')])
        .activeSections()
        .map(s => s.id)
    ).toEqual(['later']);
    expect(build([]).activeSections()).toEqual([]);
  });

  it('ignores installments and dismissed subscriptions', () => {
    const sections = build([
      sub('plan', '2026-09-25', {kind: 'installment'}),
      sub('gone', '2026-09-25', {status: 'dismissed'}),
    ]).activeSections();

    expect(sections).toEqual([]);
  });

  it('sorts each section by the selected sort', () => {
    const sections = build(
      [
        sub('a', '2026-09-24', {monthlyEquivalent: 5}),
        sub('b', '2026-09-23', {monthlyEquivalent: 50}),
      ],
      'amount'
    ).activeSections();

    expect(sections[0].items.map(i => i.id)).toEqual(['b', 'a']);
  });
});
