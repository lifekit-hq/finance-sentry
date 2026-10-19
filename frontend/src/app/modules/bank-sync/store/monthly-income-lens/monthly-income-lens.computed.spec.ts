import {signal} from '@angular/core';
import {describe, expect, it} from 'vitest';

import {type MonthlyIncomeLens} from '../../models/income-lens/income-lens.model';
import {monthlyIncomeLensComputed} from './monthly-income-lens.computed';
import {monthlyIncomeLens} from './monthly-income-lens.fixtures';

function build(lens: Nullable<MonthlyIncomeLens>) {
  return monthlyIncomeLensComputed({lens: signal(lens)});
}

describe('monthlyIncomeLensComputed', () => {
  it('is hidden and empty before the lens loads', () => {
    const computeds = build(null);
    expect(computeds.visible()).toBe(false);
    expect(computeds.monthlyIncome()).toBe('');
    expect(computeds.assumptions()).toBe('');
    expect(computeds.hasStaleSleeves()).toBe(false);
  });

  it('is hidden with no positive net worth', () => {
    expect(build(monthlyIncomeLens({status: 'NoNetWorth', monthlyIncome: null})).visible()).toBe(
      false
    );
  });

  it('flags stale sleeves only while the lens is visible', () => {
    expect(build(monthlyIncomeLens({hasStaleSleeves: true})).hasStaleSleeves()).toBe(true);
    expect(build(monthlyIncomeLens()).hasStaleSleeves()).toBe(false);
    expect(
      build(
        monthlyIncomeLens({status: 'NoNetWorth', monthlyIncome: null, hasStaleSleeves: true})
      ).hasStaleSleeves()
    ).toBe(false);
  });

  it('shows the monthly income and its assumptions in words', () => {
    const computeds = build(monthlyIncomeLens());
    expect(computeds.visible()).toBe(true);
    expect(computeds.monthlyIncome()).toBe('$1,000');
    expect(computeds.assumptions()).toContain('drawn at 4% a year');
  });
});
