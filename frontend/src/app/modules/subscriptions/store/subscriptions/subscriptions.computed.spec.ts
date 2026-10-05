import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {afterEach, beforeEach, describe, expect, it, vi} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
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
    isTracked: true,
    ...overrides,
  } satisfies Subscription;
}

function build(subscriptions: Subscription[], sort: SubscriptionSort = 'date') {
  return TestBed.runInInjectionContext(() =>
    subscriptionsComputed({
      subscriptions: signal(subscriptions),
      sort: signal(sort),
      summary: signal(null),
      addErrorCode: signal(null),
      addErrorDetail: signal(null),
    })
  );
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

  // Regression: a row whose expected charge never arrived read as "Due this week" forever,
  // because only the upper bound of the window was checked.
  it('puts a row whose expected charge date has passed under charge missed, not due', () => {
    const sections = build([
      sub('overdue', '2026-08-24'),
      sub('yesterday', '2026-09-21'),
      sub('today', '2026-09-22'),
      sub('later', '2026-10-20'),
    ]).activeSections();

    expect(sections.map(s => [s.id, s.label, s.items.map(i => i.id)])).toEqual([
      ['due', 'Due this week', ['today']],
      ['missed', 'Charge missed', ['overdue', 'yesterday']],
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

  // Regression: a legacy hand-typed row keeps the dates it was typed with, so it read as due or
  // missed although the plan is paid; it now sits in a neutral group until it is linked.
  it('puts an unlinked row in a neutral group, never due or missed', () => {
    const sections = build([
      sub('typed-overdue', '2026-08-24', {isTracked: false}),
      sub('typed-soon', '2026-09-25', {isTracked: false}),
      sub('followed', '2026-10-20'),
    ]).activeSections();

    expect(sections.map(s => [s.id, s.label, s.items.map(i => i.id)])).toEqual([
      ['later', 'Later', ['followed']],
      ['unlinked', 'Not linked to transactions', ['typed-overdue', 'typed-soon']],
    ]);
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

describe('subscriptionsComputed addErrorMessage', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  function withError(addErrorCode: Nullable<string>, addErrorDetail: Nullable<string> = null) {
    return TestBed.runInInjectionContext(() =>
      subscriptionsComputed({
        subscriptions: signal([]),
        sort: signal('date'),
        summary: signal(null),
        addErrorCode: signal(addErrorCode),
        addErrorDetail: signal(addErrorDetail),
      })
    ).addErrorMessage();
  }

  it('is empty without an error', () => {
    expect(withError(null)).toBe('');
  });

  it('resolves a registered code', () => {
    expect(withError('COMMITMENT_ALREADY_TRACKED')).toBe(
      ERROR_MESSAGES_REGISTRY['COMMITMENT_ALREADY_TRACKED']
    );
  });

  it('names the row that already holds the charges using the server text', () => {
    expect(
      withError('COMMITMENT_ALREADY_TRACKED', 'These charges are already tracked as Acme Hosting.')
    ).toBe('These charges are already tracked as Acme Hosting.');
  });

  it('ignores the server text for any other code', () => {
    expect(withError('COMMITMENT_ALREADY_LINKED', 'Something else')).toBe(
      ERROR_MESSAGES_REGISTRY['COMMITMENT_ALREADY_LINKED']
    );
  });

  it('falls back to a generic message for an unregistered code', () => {
    expect(withError('SOMETHING_ELSE')).toBe('Failed to add.');
  });
});
