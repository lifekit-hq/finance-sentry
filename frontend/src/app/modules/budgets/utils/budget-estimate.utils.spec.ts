import {describe, expect, it} from 'vitest';

import {BudgetEstimateUtils} from './budget-estimate.utils';

describe('BudgetEstimateUtils', () => {
  it('labels a month key', () => {
    expect(BudgetEstimateUtils.monthLabel('2026-09')).toBe('September 2026');
  });

  it('joins a list in plain words', () => {
    expect(BudgetEstimateUtils.joinList([])).toBe('');
    expect(BudgetEstimateUtils.joinList(['a'])).toBe('a');
    expect(BudgetEstimateUtils.joinList(['a', 'b'])).toBe('a and b');
    expect(BudgetEstimateUtils.joinList(['a', 'b', 'c'])).toBe('a, b and c');
  });

  it('says nothing when no month is left out', () => {
    expect(BudgetEstimateUtils.skippedNote([])).toBeNull();
  });

  it('explains the months set aside for the unseen card in plain words', () => {
    const note = BudgetEstimateUtils.skippedNote([
      {month: '2026-09', reason: 'excluded'},
      {month: '2026-10', reason: 'excluded'},
    ]);
    expect(note).toBe(
      'September 2026 and October 2026 are left out of these estimates: that spending went through a card this app cannot see, so those months would read far too low.'
    );
  });

  it('explains a coverage gap separately and in the singular', () => {
    expect(BudgetEstimateUtils.skippedNote([{month: '2027-03', reason: 'coverage-gap'}])).toBe(
      'March 2027 is left out because the bank link has a gap there, so a missing month is not read as a cheap one.'
    );
  });

  it('gives both sentences when both reasons occur', () => {
    const note = BudgetEstimateUtils.skippedNote([
      {month: '2026-10', reason: 'excluded'},
      {month: '2027-03', reason: 'coverage-gap'},
    ]);
    expect(note).toContain('October 2026 is left out of these estimates');
    expect(note).toContain('March 2027 is left out because the bank link has a gap');
  });

  it('measures the share of the estimate used', () => {
    expect(BudgetEstimateUtils.usedPct(50, 200)).toBe(25);
    expect(BudgetEstimateUtils.usedPct(300, 200)).toBe(100);
    expect(BudgetEstimateUtils.usedPct(10, 0)).toBe(100);
    expect(BudgetEstimateUtils.usedPct(0, 0)).toBe(0);
  });
});
