import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {ERROR_MESSAGES} from '@lifekit-hq/core';
import {beforeEach, describe, expect, it} from 'vitest';

import {ERROR_MESSAGES_REGISTRY} from '../../../../core/errors/error-messages.registry';
import {
  type FlowBreakdown,
  type FlowBreakdownItem,
  type FlowBucket,
} from '../../models/flow-breakdown/flow-breakdown.model';
import {flowBreakdownComputed} from './flow-breakdown.computed';

function item(
  bucket: FlowBucket,
  direction: 'in' | 'out',
  amountUsd: number,
  accountId = 'a1'
): FlowBreakdownItem {
  return {
    transactionId: `${bucket}-${direction}-${amountUsd}-${accountId}`,
    accountId,
    bankName: 'Monobank',
    accountLast4: '1234',
    currency: 'USD',
    amount: amountUsd,
    amountUsd,
    date: '2026-09-12',
    description: 'row',
    merchantName: null,
    category: null,
    direction,
    bucket,
    counterpartyName: null,
    flowRole: null,
  };
}

function groupsFor(items: FlowBreakdownItem[], accountFilter: Nullable<string> = null) {
  const store = {
    breakdown: signal<Nullable<FlowBreakdown>>({month: '2026-09', items}),
    month: signal('2026-09'),
    accountFilter: signal(accountFilter),
    status: signal<AsyncStatus>('idle'),
    errorCode: signal<Nullable<string>>(null),
  };
  return TestBed.runInInjectionContext(() => flowBreakdownComputed(store).groups());
}

describe('flowBreakdownComputed sharePct', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{provide: ERROR_MESSAGES, useValue: ERROR_MESSAGES_REGISTRY}],
    });
  });

  it('shares an outflow group of counted spending', () => {
    const groups = groupsFor([item('spending', 'out', 300), item('invested', 'out', 75)]);
    expect(groups.find(g => g.bucket === 'spending')?.sharePct).toBe(100);
    expect(groups.find(g => g.bucket === 'invested')?.sharePct).toBe(25);
  });

  it('shares an inflow group of income', () => {
    const groups = groupsFor([
      item('income', 'in', 1000),
      item('investment-return', 'in', 250),
      item('spending', 'out', 400),
    ]);
    expect(groups.find(g => g.bucket === 'income')?.sharePct).toBe(100);
    expect(groups.find(g => g.bucket === 'investment-return')?.sharePct).toBe(25);
  });

  it('is null when the base is zero', () => {
    const groups = groupsFor([item('invested', 'out', 50)]);
    expect(groups[0]?.sharePct).toBeNull();
  });

  it('is null for a group that mixes directions', () => {
    const groups = groupsFor([
      item('spending', 'out', 100),
      item('excluded-pair', 'out', 40),
      item('excluded-pair', 'in', 40),
    ]);
    expect(groups.find(g => g.bucket === 'excluded-pair')?.sharePct).toBeNull();
  });

  it('follows the account filter', () => {
    const groups = groupsFor(
      [
        item('spending', 'out', 100, 'a1'),
        item('invested', 'out', 50, 'a1'),
        item('spending', 'out', 900, 'a2'),
      ],
      'a1'
    );
    expect(groups.find(g => g.bucket === 'invested')?.sharePct).toBe(50);
  });
});
