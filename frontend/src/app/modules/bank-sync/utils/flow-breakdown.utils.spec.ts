import {describe, expect, it} from 'vitest';

import {type FlowBreakdownItem} from '../models/flow-breakdown/flow-breakdown.model';
import {FlowBreakdownUtils} from './flow-breakdown.utils';

const ITEM: FlowBreakdownItem = {
  transactionId: 't1',
  accountId: 'a1',
  bankName: 'Monobank',
  accountLast4: '1234',
  currency: 'UAH',
  amount: 4100,
  amountUsd: 100,
  date: '2026-09-12',
  description: 'Silpo groceries',
  merchantName: null,
  category: 'groceries',
  direction: 'out',
  bucket: 'spending',
  counterpartyName: null,
  flowRole: null,
};

describe('FlowBreakdownUtils.toTransaction', () => {
  it('maps an outflow row to a debit ledger transaction', () => {
    const tx = FlowBreakdownUtils.toTransaction(ITEM);

    expect(tx).toMatchObject({
      transactionId: 't1',
      accountId: 'a1',
      transactionType: 'debit',
      merchantCategory: 'groceries',
      isPending: false,
    });
  });

  it('maps an inflow row to a credit', () => {
    expect(FlowBreakdownUtils.toTransaction({...ITEM, direction: 'in'}).transactionType).toBe(
      'credit'
    );
  });
});

describe('FlowBreakdownUtils.monthDates', () => {
  it('spans the whole month', () => {
    expect(FlowBreakdownUtils.monthDates('2026-09')).toEqual({
      from: '2026-09-01',
      to: '2026-09-30',
    });
  });

  it('handles a leap February', () => {
    expect(FlowBreakdownUtils.monthDates('2028-02').to).toBe('2028-02-29');
  });

  it('returns no bounds for an empty key', () => {
    expect(FlowBreakdownUtils.monthDates('')).toEqual({});
  });
});

describe('FlowBreakdownUtils.counterpartyQuery', () => {
  it('searches the counterparty name when the row spells it, case-insensitively', () => {
    expect(
      FlowBreakdownUtils.counterpartyQuery({
        ...ITEM,
        counterpartyName: ' Anna K ',
        description: 'Від: ANNA K rent',
      })
    ).toBe('Anna K');
    expect(
      FlowBreakdownUtils.counterpartyQuery({
        ...ITEM,
        counterpartyName: 'Anna K',
        merchantName: 'anna k',
      })
    ).toBe('Anna K');
  });

  it('returns null when neither merchant nor description spells the name, or there is none', () => {
    expect(
      FlowBreakdownUtils.counterpartyQuery({...ITEM, counterpartyName: 'Landlord'})
    ).toBeNull();
    expect(FlowBreakdownUtils.counterpartyQuery({...ITEM, counterpartyName: null})).toBeNull();
    expect(FlowBreakdownUtils.counterpartyQuery({...ITEM, counterpartyName: '  '})).toBeNull();
  });
});
