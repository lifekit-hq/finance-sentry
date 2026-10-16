import {describe, expect, it} from 'vitest';

import {type LedgerFilter} from '../store/transaction-ledger/transaction-ledger.computed';
import {LedgerFilterUtils} from './ledger-filter.utils';

const BASE: LedgerFilter = {
  accountId: 'acc-1',
  transactionType: 'debit',
  categories: ['FOOD', 'RENT'],
  from: '2026-06-01',
  to: '2026-08-31',
  search: 'netflix',
};

describe('LedgerFilterUtils', () => {
  describe('same', () => {
    it('matches an equal filter', () => {
      expect(LedgerFilterUtils.same(BASE, {...BASE, categories: ['FOOD', 'RENT']})).toBe(true);
    });

    it.each<[string, Partial<LedgerFilter>]>([
      ['account', {accountId: 'acc-2'}],
      ['type', {transactionType: 'credit'}],
      ['categories', {categories: ['FOOD']}],
      ['from', {from: '2026-07-01'}],
      ['to', {to: null}],
      ['search', {search: ''}],
    ])('differs on %s', (_name, change) => {
      expect(LedgerFilterUtils.same(BASE, {...BASE, ...change})).toBe(false);
    });

    it('matches nothing when a filter is absent', () => {
      expect(LedgerFilterUtils.same(null, BASE)).toBe(false);
      expect(LedgerFilterUtils.same(BASE, null)).toBe(false);
      expect(LedgerFilterUtils.same(null, null)).toBe(false);
    });
  });
});
