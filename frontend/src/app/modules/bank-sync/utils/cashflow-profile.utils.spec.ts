import {describe, expect, it} from 'vitest';

import {type CashflowProfile} from '../models/cashflow-profile/cashflow-profile.model';
import {CashflowProfileUtils} from './cashflow-profile.utils';

function profile(overrides: Partial<CashflowProfile> = {}): CashflowProfile {
  return {
    status: 'Shown',
    completeMonths: 6,
    fromMonth: '2026-03',
    toMonth: '2026-08',
    incomeMonthlyMedian: 4_000,
    burnMonthlyMedian: 3_000,
    savingsRate: 0.25,
    runwayMonths: 6,
    runwayReason: null,
    committedShare: 0.2,
    hasStaleSleeves: false,
    reason: null,
    baseCurrency: 'USD',
    disclosure: 'Context only.',
    ...overrides,
  };
}

describe('CashflowProfileUtils', () => {
  it('labels a month key as month and year', () => {
    expect(CashflowProfileUtils.monthLabel('2026-03')).toBe('Mar 2026');
  });

  it.each([
    [0.25, '25%'],
    [0.3827, '38%'],
    [-0.1, '-10%'],
    [1, '100%'],
  ])('writes the rate %s as %s', (rate, label) => {
    expect(CashflowProfileUtils.rateLabel(rate)).toBe(label);
  });

  it('never writes a negative zero', () => {
    expect(CashflowProfileUtils.rateLabel(-0.001)).toBe('0%');
    expect(CashflowProfileUtils.rateLabel(0)).toBe('0%');
  });

  it.each([
    [6, '6 months'],
    [6.5, '6.5 months'],
    [6.04, '6 months'],
    [1, '1 month'],
    [0.4, '0.4 months'],
  ])('writes the runway %s as %s', (months, label) => {
    expect(CashflowProfileUtils.runwayLabel(months)).toBe(label);
  });

  it('says nothing about the runway while there is one', () => {
    expect(CashflowProfileUtils.runwayNote(profile())).toBeNull();
  });

  it.each([
    ['NO_SNAPSHOT', 'No net-worth snapshot yet'],
    ['NO_CASH_SPLIT', "doesn't split out cash"],
    ['NO_CASH', 'No cash in your latest snapshot'],
    ['NO_BURN', 'No spending in these months'],
    ['SOMETHING_NEW', 'unavailable right now'],
  ])('explains a missing runway for %s', (runwayReason, expected) => {
    const note = CashflowProfileUtils.runwayNote(profile({runwayMonths: null, runwayReason}));
    expect(note).toContain(expected);
  });

  it('words a negative rate as spending more than came in', () => {
    expect(CashflowProfileUtils.rateNote(-0.2)).toBe('spending more than came in');
    expect(CashflowProfileUtils.rateNote(0.2)).toBe('of income kept');
  });

  it('asks for the history that is missing', () => {
    const text = CashflowProfileUtils.emptySubMessage(
      profile({status: 'InsufficientHistory', completeMonths: 2, reason: 'INSUFFICIENT_MONTHS'})
    );
    expect(text).toContain('at least 3 complete months');
    expect(text).toContain('2 so far');
  });

  it('asks for the history before one has loaded', () => {
    expect(CashflowProfileUtils.emptySubMessage(null)).toContain('0 so far');
  });

  it('names the missing income when that is why nothing is shown', () => {
    const text = CashflowProfileUtils.emptySubMessage(
      profile({status: 'InsufficientHistory', reason: 'NO_POSITIVE_INCOME'})
    );
    expect(text).toContain('No income came in');
  });

  it('spells out the window, both medians and what the rate and runway divide', () => {
    const text = CashflowProfileUtils.assumptions(profile());
    expect(text).toContain('median over the 6 complete months from Mar 2026 to Aug 2026');
    expect(text).toContain('$4,000 in, $3,000 out');
    expect(text).toContain('everything saved divided by everything earned');
    expect(text).toContain("divided by a typical month's spending");
    expect(text).toContain('not a forecast or advice');
  });

  it('states the amounts in the base currency', () => {
    const text = CashflowProfileUtils.assumptions(
      profile({baseCurrency: 'EUR', incomeMonthlyMedian: 3_700, burnMonthlyMedian: 2_800})
    );
    expect(text).toContain('€3,700 in, €2,800 out');
  });

  it('still states the window without month names', () => {
    const text = CashflowProfileUtils.assumptions(profile({fromMonth: null, toMonth: null}));
    expect(text).toContain('median over the 6 complete months:');
  });
});
