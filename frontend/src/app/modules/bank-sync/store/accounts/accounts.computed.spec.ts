import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {
  type CategorySummary,
  type WealthSummaryResponse,
} from '../../../../shared/models/wealth/wealth.model';
import {accountsComputed} from './accounts.computed';

function category(name: CategorySummary['category']): CategorySummary {
  return {category: name, totalInBaseCurrency: 100, institutionCount: 1, institutions: []};
}

function computedFor(categories: CategorySummary[]) {
  return accountsComputed({
    summary: signal({
      totalNetWorth: 0,
      baseCurrency: 'USD',
      categories,
    } as unknown as WealthSummaryResponse),
    status: signal('success' as const),
  });
}

describe('accountsComputed.categorySections', () => {
  it('orders sections banking, brokerage, crypto whatever order the API sends', () => {
    const c = computedFor([category('crypto'), category('banking'), category('brokerage')]);

    expect(c.categorySections().map(s => s.category)).toEqual(['banking', 'brokerage', 'crypto']);
  });

  it('skips categories the user has nothing in', () => {
    const c = computedFor([category('crypto')]);

    expect(c.categorySections().map(s => s.category)).toEqual(['crypto']);
  });

  it('names the rows of each section', () => {
    const c = computedFor([category('banking'), category('brokerage')]);

    expect(c.categorySections().map(s => s.rowNoun)).toEqual(['account', 'position']);
  });
});
