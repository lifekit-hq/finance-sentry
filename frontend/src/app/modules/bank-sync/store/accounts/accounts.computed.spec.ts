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
    allAccounts: signal([]),
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

describe('accountsComputed net-worth breakdown', () => {
  const withTotals = (banking: number, brokerage: number, crypto: number) =>
    computedFor([
      {...category('banking'), totalInBaseCurrency: banking},
      {...category('brokerage'), totalInBaseCurrency: brokerage},
      {...category('crypto'), totalInBaseCurrency: crypto},
    ]);

  it('keeps a negative category as a row with no percent', () => {
    const rows = withTotals(-93.23, 9278.96, 6489.6).netWorthBreakdown();

    expect(rows.map(r => r.label)).toEqual(['Brokerage', 'Crypto', 'Banking']);
    expect(rows[2].value).toBe(-93.23);
    expect(rows[2].percent).toBeNull();
  });

  it('takes the percent of the positive total only', () => {
    const rows = withTotals(-100, 75, 25).netWorthBreakdown();

    expect(rows.slice(0, 2).map(r => r.percent)).toEqual([75, 25]);
  });

  it('drops a category that nets to zero', () => {
    const rows = withTotals(0, 50, 50).netWorthBreakdown();

    expect(rows.map(r => r.label)).toEqual(['Brokerage', 'Crypto']);
  });

  it('keeps only positive categories in the donut', () => {
    const segments = withTotals(-93.23, 9278.96, 6489.6).netWorthSegments();

    expect(segments.map(s => s.label)).toEqual(['Brokerage', 'Crypto']);
  });
});
