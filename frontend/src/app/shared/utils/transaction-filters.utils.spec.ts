import {describe, expect, it} from 'vitest';

import {EMPTY_TRANSACTION_FILTERS} from '../constants/transaction-filters/transaction-filters.constants';
import {TransactionFiltersUtils} from './transaction-filters.utils';

describe('TransactionFiltersUtils.isActive', () => {
  it('is false for the empty filters', () => {
    expect(TransactionFiltersUtils.isActive(EMPTY_TRANSACTION_FILTERS)).toBe(false);
  });

  it('is false for a whitespace-only search', () => {
    expect(TransactionFiltersUtils.isActive({...EMPTY_TRANSACTION_FILTERS, search: '  '})).toBe(
      false
    );
  });

  it.each([
    {accountIds: ['acc-1']},
    {categories: ['FOOD_AND_DRINK']},
    {transactionType: 'credit' as const},
    {from: '2026-01-01'},
    {to: '2026-01-31'},
    {minAmount: 0},
    {maxAmount: 100},
    {search: 'coffee'},
  ])('is true when %o is set', patch => {
    expect(TransactionFiltersUtils.isActive({...EMPTY_TRANSACTION_FILTERS, ...patch})).toBe(true);
  });
});

describe('TransactionFiltersUtils.parseAmount', () => {
  it('clears the bound for null', () => {
    expect(TransactionFiltersUtils.parseAmount(null)).toBeNull();
  });

  it('clears the bound for blank text', () => {
    expect(TransactionFiltersUtils.parseAmount('  ')).toBeNull();
  });

  it('clears the bound for non-numeric text', () => {
    expect(TransactionFiltersUtils.parseAmount('abc')).toBeNull();
  });

  it('parses a decimal', () => {
    expect(TransactionFiltersUtils.parseAmount('12.5')).toBe(12.5);
  });
});

describe('TransactionFiltersUtils.parseType', () => {
  it.each(['credit', 'debit'] as const)('keeps %s', type => {
    expect(TransactionFiltersUtils.parseType(type)).toBe(type);
  });

  it.each([null, '', 'foo', 'CREDIT'])('treats %s as no filter', raw => {
    expect(TransactionFiltersUtils.parseType(raw)).toBeNull();
  });
});

describe('TransactionFiltersUtils.parseDate', () => {
  it('keeps a yyyy-MM-dd date', () => {
    expect(TransactionFiltersUtils.parseDate('2026-08-10')).toBe('2026-08-10');
  });

  it.each([null, '', 'garbage', '2026-8-1', '2026-08-10T00:00:00Z'])(
    'treats %s as no bound',
    raw => {
      expect(TransactionFiltersUtils.parseDate(raw)).toBeNull();
    }
  );
});

describe('TransactionFiltersUtils.parseAmount', () => {
  it.each([
    ['50', 50],
    ['0', 0],
    ['1.05', 1.05],
    ['', null],
    ['  ', null],
    ['abc', null],
    ['NaN', null],
    [null, null],
  ])('parses %j to %j', (raw, expected) => {
    expect(TransactionFiltersUtils.parseAmount(raw)).toBe(expected);
  });
});

describe('TransactionFiltersUtils.syncInputText', () => {
  const committed = {minAmount: null, maxAmount: null, search: ''};
  const typed = {minAmount: '1.0', maxAmount: '', search: ' coffee'};

  it('starts from the committed filters', () => {
    expect(
      TransactionFiltersUtils.syncInputText({minAmount: 5, maxAmount: null, search: 'tea'})
    ).toEqual({minAmount: '5', maxAmount: '', search: 'tea'});
  });

  it('keeps text that still means the committed value', () => {
    expect(
      TransactionFiltersUtils.syncInputText({...committed, minAmount: 1, search: 'coffee'}, typed)
    ).toEqual(typed);
  });

  it('follows the filters when they changed under the text', () => {
    expect(TransactionFiltersUtils.syncInputText(committed, typed)).toEqual({
      minAmount: '',
      maxAmount: '',
      search: '',
    });
  });
});
