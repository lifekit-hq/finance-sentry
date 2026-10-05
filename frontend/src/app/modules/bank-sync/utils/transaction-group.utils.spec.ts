import {describe, expect, it} from 'vitest';

import {type GlobalTransactionDto} from '../models/transaction/transaction.model';
import {TransactionGroupUtils} from './transaction-group.utils';

const NOW = new Date(2026, 7, 10, 12, 0, 0);

function tx(id: string, date: string, postedDate: string | null = date): GlobalTransactionDto {
  return {
    transactionId: id,
    accountId: 'acc-1',
    bankName: 'Test Bank',
    currency: 'USD',
    amount: 10,
    amountUsd: 10,
    date,
    postedDate,
    description: id,
    transactionType: 'debit',
    merchantCategory: null,
    isPending: false,
    createdAt: date,
  };
}

describe('TransactionGroupUtils.dayKey', () => {
  // A bank reports a booking day as its local midnight. In any zone ahead of UTC (Dublin is
  // UTC+1 in October) that instant is the previous evening in UTC, so slicing the UTC text puts
  // the row a day early. The fixture is local midnight, so the expectation holds in every zone.
  const bankMidnight = new Date(2026, 9, 2, 0, 0, 0).toISOString();

  it("keeps a bank's local midnight on its own day (booked row)", () => {
    expect(TransactionGroupUtils.dayKey(tx('atm', bankMidnight, bankMidnight))).toBe('2026-10-02');
  });

  it("keeps a bank's local midnight on its own day (pending row, no posted date)", () => {
    expect(TransactionGroupUtils.dayKey(tx('mobi', bankMidnight, null))).toBe('2026-10-02');
  });

  it('groups both rows into one Oct 2 day rather than Oct 1', () => {
    const groups = TransactionGroupUtils.groupByDay(
      [tx('atm', bankMidnight, bankMidnight), tx('mobi', bankMidnight, null)],
      NOW
    );
    expect(groups.map(g => g.dayKey)).toEqual(['2026-10-02']);
  });

  it('falls back to the leading date text when the value is not parseable', () => {
    expect(TransactionGroupUtils.dayKey(tx('a', '2026-10-02Tgarbage', null))).toBe('2026-10-02');
  });

  it('treats a bare calendar date as that day', () => {
    expect(TransactionGroupUtils.dayKey(tx('a', '2026-10-02', '2026-10-02'))).toBe('2026-10-02');
  });

  it('prefers the posted date', () => {
    expect(TransactionGroupUtils.dayKey(tx('a', '2026-08-09T10:00:00Z', '2026-08-10'))).toBe(
      '2026-08-10'
    );
  });

  it('falls back to the transaction date for pending rows', () => {
    expect(TransactionGroupUtils.dayKey(tx('a', '2026-08-09T10:00:00Z', null))).toBe('2026-08-09');
  });
});

describe('TransactionGroupUtils.dayLabel', () => {
  it('labels the current day', () => {
    expect(TransactionGroupUtils.dayLabel('2026-08-10', NOW)).toBe('Today');
  });

  it('labels the previous day', () => {
    expect(TransactionGroupUtils.dayLabel('2026-08-09', NOW)).toBe('Yesterday');
  });

  it('uses weekday, month and day within the current year', () => {
    expect(TransactionGroupUtils.dayLabel('2026-08-03', NOW)).toBe('Mon, Aug 3');
  });

  it('adds the year outside the current year', () => {
    expect(TransactionGroupUtils.dayLabel('2025-12-31', NOW)).toBe('Wed, Dec 31, 2025');
  });

  it('echoes an unparseable key', () => {
    expect(TransactionGroupUtils.dayLabel('nope', NOW)).toBe('nope');
  });
});

describe('TransactionGroupUtils.groupByDay', () => {
  it('returns no groups for an empty page', () => {
    expect(TransactionGroupUtils.groupByDay([], NOW)).toEqual([]);
  });

  it('groups by day and keeps the incoming order', () => {
    const groups = TransactionGroupUtils.groupByDay(
      [tx('a', '2026-08-10'), tx('b', '2026-08-10'), tx('c', '2026-08-09')],
      NOW
    );
    expect(groups.map(g => [g.label, g.items.map(i => i.transactionId)])).toEqual([
      ['Today', ['a', 'b']],
      ['Yesterday', ['c']],
    ]);
  });
});

describe('TransactionGroupUtils account labels', () => {
  it('appends the last four digits when present', () => {
    expect(TransactionGroupUtils.accountLabel({bankName: 'Bank', accountNumberLast4: '1234'})).toBe(
      'Bank · 1234'
    );
  });

  it('uses the bank name alone without digits', () => {
    expect(TransactionGroupUtils.accountLabel({bankName: 'Bank', accountNumberLast4: ''})).toBe(
      'Bank'
    );
  });

  it('maps accounts to filter options', () => {
    expect(
      TransactionGroupUtils.toAccountOptions([
        {accountId: 'a1', bankName: 'Bank', accountNumberLast4: '9'},
      ])
    ).toEqual([{accountId: 'a1', label: 'Bank · 9'}]);
  });
});
