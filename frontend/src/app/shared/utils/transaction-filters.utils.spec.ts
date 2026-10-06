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
