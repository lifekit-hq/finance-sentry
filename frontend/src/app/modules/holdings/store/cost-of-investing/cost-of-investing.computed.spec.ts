import {signal} from '@angular/core';
import {type Nullable} from '@lifekit-hq/ui';
import {describe, expect, it} from 'vitest';

import {type SleeveCosts} from '../../models/cost-of-investing/cost-of-investing.model';
import {costOfInvestingComputed} from './cost-of-investing.computed';

const NO_COSTS: SleeveCosts = {
  asOf: '2026-11-24',
  yearStart: '2026-01-01',
  commissions: 0,
  fees: 0,
  fxConversion: 0,
  withholding: 0,
  total: 0,
  shareOfReturns: null,
  unvaluedRows: 0,
  approximateRows: 0,
  fxReasons: [],
  currency: 'USD',
};

const hasCostsFor = (costs: Nullable<SleeveCosts>): boolean =>
  costOfInvestingComputed({costs: signal(costs)}).hasCosts();

describe('costOfInvestingComputed.hasCosts', () => {
  it('hides the row before the costs load', () => {
    expect(hasCostsFor(null)).toBe(false);
  });

  it('hides the row for a book with no cost rows at all', () => {
    expect(hasCostsFor(NO_COSTS)).toBe(false);
  });

  it('shows the row once something was paid', () => {
    expect(hasCostsFor({...NO_COSTS, commissions: 12, total: 12})).toBe(true);
  });

  it('shows the row when every fee was left out for want of a USD value, so the user sees fees went uncounted', () => {
    expect(hasCostsFor({...NO_COSTS, unvaluedRows: 3})).toBe(true);
  });

  it('shows the row when the only rows were converted without a published rate', () => {
    expect(hasCostsFor({...NO_COSTS, approximateRows: 1})).toBe(true);
  });
});
