import {describe, expect, it} from 'vitest';

import {monthlyIncomeLens} from '../store/monthly-income-lens/monthly-income-lens.fixtures';
import {IncomeLensUtils} from './income-lens.utils';

describe('IncomeLensUtils', () => {
  describe('monthlyIncome', () => {
    it('formats the income in whole units of the base currency', () => {
      expect(IncomeLensUtils.monthlyIncome(monthlyIncomeLens({monthlyIncome: 1_666.67}))).toBe(
        '$1,667'
      );
      expect(
        IncomeLensUtils.monthlyIncome(monthlyIncomeLens({monthlyIncome: 925, baseCurrency: 'EUR'}))
      ).toBe('€925');
    });

    it('is an em dash when there is no income', () => {
      expect(
        IncomeLensUtils.monthlyIncome(
          monthlyIncomeLens({status: 'NoNetWorth', monthlyIncome: null})
        )
      ).toBe('—');
    });
  });

  describe('assumptions', () => {
    it('states the total, the withdrawal rate and the disclosure in words', () => {
      expect(IncomeLensUtils.assumptions(monthlyIncomeLens())).toBe(
        'Your $300,000 net worth drawn at 4% a year, the safe withdrawal rate your ' +
          "financial-independence target uses. In today's money. Hypothetical, not a guarantee."
      );
    });

    it("reads the person's own withdrawal rate", () => {
      expect(IncomeLensUtils.assumptions(monthlyIncomeLens({safeWithdrawalRate: 0.035}))).toContain(
        'drawn at 3.5% a year'
      );
    });
  });
});
